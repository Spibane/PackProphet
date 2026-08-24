namespace PackProphet.Deck;

/// <summary>
/// The Pokémon TCG Pocket deck-share code. The in-game "share deck" 2D code is a base64
/// binary blob in this format, reverse-engineered by Nirostar/ptcgp-deck-qr (MIT) and
/// ported here so the project owns it outright and ships no JS dependency.
///
/// Binary layout, before base64:
///
///     [ trainer segment ][ pokémon segment ][ energy block ]
///
///     Segment:      1 byte count N, then N x 3-byte big-endian (DeckBuilderNr x 10).
///                   An empty segment is a single 0x00. Duplicates are listed twice.
///                   Order is irrelevant to the scanner; ascending is emitted for stability.
///     Energy block: 1 byte count (0-3), then one id per energy type.
///
/// This is the one part of the app tracking a format the game can change. Keeping it in a single
/// file makes that a one-file fix.
/// </summary>
public static class DeckCodec
{
    public const int MaxEnergyTypes = 3;

    /// <summary>Energy ids as used by the format.</summary>
    public static readonly IReadOnlyDictionary<EnergyType, int> EnergyIds = new Dictionary<EnergyType, int>
    {
        [EnergyType.Grass] = 1,
        [EnergyType.Fire] = 2,
        [EnergyType.Water] = 3,
        [EnergyType.Lightning] = 4,
        [EnergyType.Psychic] = 5,
        [EnergyType.Fighting] = 6,
        [EnergyType.Darkness] = 7,
        [EnergyType.Metal] = 8,
    };

    private static readonly Dictionary<int, EnergyType> ById =
        EnergyIds.ToDictionary(kv => kv.Value, kv => kv.Key);

    /// <summary>Encode a deck to its share code.</summary>
    /// <param name="deckBuilderNrs">One entry per copy.</param>
    public static string Create(IEnumerable<int> deckBuilderNrs, IEnumerable<EnergyType> energies)
    {
        var nrs = deckBuilderNrs.ToArray();
        if (nrs.Any(n => n <= 0))
            throw new ArgumentException("DeckBuilderNrs must be positive.", nameof(deckBuilderNrs));

        var energyList = energies.ToArray();
        if (energyList.Length > MaxEnergyTypes)
            throw new ArgumentException($"At most {MaxEnergyTypes} energy types.", nameof(energies));

        var trainers = nrs.Where(DeckBuilderNr.IsTrainer).OrderBy(n => n).ToArray();
        var pokemon = nrs.Where(n => !DeckBuilderNr.IsTrainer(n)).OrderBy(n => n).ToArray();

        var bytes = new List<byte>(2 + (nrs.Length * 3) + 1 + energyList.Length);
        foreach (var segment in new[] { trainers, pokemon })
        {
            if (segment.Length > byte.MaxValue)
                throw new ArgumentException("Segment holds more than 255 cards.", nameof(deckBuilderNrs));

            bytes.Add((byte)segment.Length);
            foreach (var nr in segment)
            {
                var v = nr * 10;
                if (v > 0xFFFFFF)
                    throw new ArgumentException($"DeckBuilderNr {nr} does not fit in 3 bytes.", nameof(deckBuilderNrs));
                bytes.Add((byte)((v >> 16) & 0xFF));
                bytes.Add((byte)((v >> 8) & 0xFF));
                bytes.Add((byte)(v & 0xFF));
            }
        }

        bytes.Add((byte)energyList.Length);
        foreach (var e in energyList) bytes.Add((byte)EnergyIds[e]);

        return Convert.ToBase64String(bytes.ToArray());
    }

    /// <summary>Decode a share code. Throws <see cref="FormatException"/> on malformed input.</summary>
    public static ParsedDeck Parse(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new FormatException("Deck code is empty.");

        byte[] raw;
        try { raw = Convert.FromBase64String(code.Trim()); }
        catch (FormatException) { throw new FormatException("Deck code is not valid base64."); }

        var i = 0;
        var trainers = ReadSegment(raw, ref i);
        var pokemon = ReadSegment(raw, ref i);

        var energies = new List<EnergyType>();
        var unknownEnergyIds = new List<int>();
        if (i < raw.Length)
        {
            var count = raw[i++];
            if (count > MaxEnergyTypes)
                throw new FormatException($"Deck code declares {count} energy types; the limit is {MaxEnergyTypes}.");
            for (var k = 0; k < count; k++)
            {
                if (i >= raw.Length) throw new FormatException("Deck code ends mid-energy-block.");
                var id = raw[i++];
                // Tolerate an unrecognised id: a new energy type would otherwise make every
                // deck using it unimportable. Surface it instead of throwing.
                if (ById.TryGetValue(id, out var e)) energies.Add(e);
                else unknownEnergyIds.Add(id);
            }
        }

        return new ParsedDeck(trainers, pokemon, energies, unknownEnergyIds);
    }

    /// <summary>Parse without throwing. Returns null when the code is malformed.</summary>
    public static ParsedDeck? TryParse(string code)
    {
        try { return Parse(code); }
        catch (FormatException) { return null; }
    }

    private static List<int> ReadSegment(byte[] raw, ref int i)
    {
        if (i >= raw.Length) throw new FormatException("Deck code ends before a card segment.");

        var count = raw[i++];
        var result = new List<int>(count);
        for (var k = 0; k < count; k++)
        {
            if (i + 3 > raw.Length)
                throw new FormatException("Deck code ends mid-card; it is truncated.");

            var v = (raw[i] << 16) | (raw[i + 1] << 8) | raw[i + 2];
            i += 3;
            if (v % 10 != 0)
                throw new FormatException($"Card value {v} is not a multiple of ten; the format has changed.");

            // Symmetric with Create, which refuses a non-positive identity. Accepting one here let
            // a deck be saved holding a card that can never resolve against the index: it shows as
            // a permanently missing card with no name.
            if (v <= 0)
                throw new FormatException("Deck code contains a zero card identity.");

            result.Add(v / 10);
        }
        return result;
    }
}

public enum EnergyType
{
    Grass, Fire, Water, Lightning, Psychic, Fighting, Darkness, Metal
}

/// <param name="Trainers">Trainer card identities, one entry per copy.</param>
/// <param name="Pokemon">Pokémon card identities, one entry per copy.</param>
/// <param name="Energies">Recognised energy types.</param>
/// <param name="UnknownEnergyIds">Ids the format did not recognise, surfaced rather than dropped.</param>
public sealed record ParsedDeck(
    IReadOnlyList<int> Trainers,
    IReadOnlyList<int> Pokemon,
    IReadOnlyList<EnergyType> Energies,
    IReadOnlyList<int> UnknownEnergyIds)
{
    /// <summary>All card identities, one entry per copy.</summary>
    public IReadOnlyList<int> AllCards => [.. Trainers, .. Pokemon];

    public int CardCount => Trainers.Count + Pokemon.Count;
}
