namespace PackProphet.State;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Identity for a logged row, and the one definition of what makes two rows the same thing.
///
/// Two jobs, and they answer different questions. <see cref="New"/> names a row nobody has seen
/// before. <see cref="Derive"/> names a row that already exists but predates ids, and has to
/// produce the SAME name on every device holding that row -- a migration that handed out random
/// ids would turn one pack into one pack per device the moment they next synced, which is the
/// fault it exists to end.
/// </summary>
public static class LogId
{
    /// <summary>Twelve hex characters. Collisions are the birthday problem at 48 bits, against a
    /// log of a few thousand rows.</summary>
    private const int Length = 12;

    public static string New() => Guid.NewGuid().ToString("n")[..Length];

    /// <summary>
    /// The id an existing row gets: a hash of what it holds, so two devices migrating their own
    /// copy of one row agree without talking.
    /// </summary>
    /// <param name="occurrence">
    /// Which row this is, among rows with identical contents. A log can legitimately hold two of
    /// those -- the same pack, opened twice inside one second -- and they need different ids.
    /// Both devices number them in log order and both hold the same rows, so both arrive at the
    /// same pair of ids.
    /// </param>
    public static string Derive(string content, int occurrence)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{content}#{occurrence}"));
        return Convert.ToHexString(hash)[..Length].ToLowerInvariant();
    }

    /// <summary>
    /// What a logged pack holds, as one string.
    ///
    /// The timestamp alone is not enough -- a ten-pack burst can share a second -- and the cards
    /// pulled are what make two rows at the same instant different.
    /// </summary>
    public static string Content(PackOpenEvent e) =>
        $"{e.At.ToUnixTimeMilliseconds()}|{e.Set}|{e.Pack}|{e.Variant}|{string.Join(",", e.OwnershipKeys)}";

    public static string Content(WonderOfferEvent e) =>
        $"{e.At.ToUnixTimeMilliseconds()}|{e.StaminaCost}|{e.Taken}|{e.Received}|{string.Join(",", e.OwnershipKeys)}";

    /// <summary>
    /// The id to merge a row by. Falls back to the contents for a row that somehow has none:
    /// a merge is not the place to discover a missing invariant, and content identity is what
    /// this did before ids existed, so the fallback is the old behaviour rather than a crash.
    /// </summary>
    public static string Of(PackOpenEvent e) => e.Id is { Length: > 0 } id ? id : Content(e);

    public static string Of(WonderOfferEvent e) => e.Id is { Length: > 0 } id ? id : Content(e);
}
