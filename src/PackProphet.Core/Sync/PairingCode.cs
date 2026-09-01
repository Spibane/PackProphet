namespace PackProphet.Sync;

using System.Security.Cryptography;

/// <summary>
/// The twelve characters a user types on their second device.
///
/// The code is the whole credential: the document id the server files the blob under and the key
/// it is encrypted with are both derived from it (see js/sync.js), so there is no account, no
/// token and nothing for the user to remember beyond this. That also means losing it loses the
/// remote copy, which the UI has to say plainly.
///
/// Crockford base32, which is the point: no I, L, O or U, so nothing in the alphabet can be
/// misread as something else on a phone screen, and the characters people still get wrong -- O for
/// zero, I or L for one -- have exactly one sensible reading and are mapped on the way in.
/// </summary>
public static class PairingCode
{
    /// <summary>32 symbols, deliberately missing I, L, O and U.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Characters carrying entropy. The thirteenth is a check character.</summary>
    public const int DataChars = 11;

    public const int TotalChars = DataChars + 1;

    /// <summary>
    /// 55 bits, in eleven characters. Enough that guessing a live document is hopeless -- an
    /// attacker has to guess the code, run the derivation, and be rate-limited by the host on
    /// every attempt -- while still being something a person will type twice without resenting it.
    /// </summary>
    public static string New()
    {
        var chars = new char[DataChars];
        // Rejection-free because the alphabet is exactly 32 symbols: five bits map to one symbol
        // with no modulo bias to correct for.
        var bytes = new byte[DataChars];
        RandomNumberGenerator.Fill(bytes);
        for (var i = 0; i < DataChars; i++) chars[i] = Alphabet[bytes[i] & 31];

        var body = new string(chars);
        return body + CheckChar(body);
    }

    /// <summary>
    /// Group into threes for display: PACK-style four-four-four reads and dictates far better
    /// than twelve run together, and the groups are cosmetic -- <see cref="TryParse"/> ignores
    /// them entirely.
    /// </summary>
    public static string Format(string code) =>
        code.Length != TotalChars
            ? code
            : $"{code[..4]}-{code[4..8]}-{code[8..]}";

    /// <summary>
    /// Normalise what the user typed, or null if it cannot be a code.
    ///
    /// Rejecting here rather than at the network boundary is most of the value: a mistyped code
    /// derives a document id that simply does not exist, and "nothing found" is indistinguishable
    /// from "you typed it wrong" once it has left the device.
    /// </summary>
    public static string? TryParse(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;

        var chars = new List<char>(TotalChars);
        foreach (var raw in typed)
        {
            // Spaces, dashes and the grouping the UI itself inserted.
            if (raw is '-' or ' ' or '\t' or '–' or '—') continue;

            var c = char.ToUpperInvariant(raw);
            c = c switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                _ => c,
            };

            if (Alphabet.IndexOf(c) < 0) return null;   // includes U, which has no reading
            chars.Add(c);
            if (chars.Count > TotalChars) return null;
        }

        if (chars.Count != TotalChars) return null;

        var code = new string(chars.ToArray());
        return CheckChar(code[..DataChars]) == code[^1] ? code : null;
    }

    /// <summary>
    /// Position-weighted sum mod 32, with ODD weights.
    ///
    /// The weights have to be odd, and that is the whole design. Every odd number is invertible
    /// mod 32, so a single mistyped character always moves the sum and is always caught. Even
    /// weights are not: with weight 2, misreading a character as the one sixteen places along
    /// leaves the sum unchanged, and the code would sail through to the network looking valid.
    ///
    /// Position weighting also catches a transposition, which is the mistake people make reading
    /// a code aloud -- an unweighted sum would not. Adjacent swaps move the sum by twice the
    /// difference between the two characters, so the one family that escapes is a swap of two
    /// characters exactly sixteen apart in the alphabet. No linear check over 32 symbols can catch
    /// those, and buying them would cost a thirteenth character; single-character errors, which
    /// are far more common, are covered completely.
    /// </summary>
    private static char CheckChar(string body)
    {
        var sum = 0;
        for (var i = 0; i < body.Length; i++) sum += (2 * i + 1) * Alphabet.IndexOf(body[i]);
        return Alphabet[sum % 32];
    }
}
