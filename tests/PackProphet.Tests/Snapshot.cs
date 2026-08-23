using System.Text.Json;
using PackProphet.Data;
using PackProphet.Engine;
using PackProphet.Domain;

namespace PackProphet.Tests;

/// <summary>Loads the same vendored data snapshot the app ships.</summary>
internal static class Snapshot
{
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);

    private static T Load<T>(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "snapshot", file);
        Assert.True(File.Exists(path), $"snapshot missing: {path}");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Opts)
               ?? throw new InvalidOperationException($"{file} deserialized to null");
    }

    private static List<PocketCard>? _cards;
    private static Dictionary<string, Rarity>? _rarities;
    private static CardIndex? _index;
    private static PullRates? _rates;
    private static PackOdds? _odds;
    private static CardFacts? _facts;

    public static List<PocketCard> Cards() => _cards ??= Load<List<PocketCard>>("cards.min.json");

    public static Dictionary<string, Rarity> Rarities() =>
        _rarities ??= Load<Dictionary<string, Rarity>>("rarities.json");

    public static CardIndex Index() => _index ??= new CardIndex(Cards(), Rarities());

    public static PullRates Rates() => _rates ??= new PullRates(
        Load<Dictionary<string, Dictionary<string, PackVariant>>>("pullRates.json"));

    public static PackOdds Odds() => _odds ??= new PackOdds(Index(), Rates());

    /// <summary>Build a rarity selection from rarity codes. Test convenience only.</summary>
    public static IReadOnlySet<int> Tiers(params string[] rarityCodes) =>
        rarityCodes.Select(c => Index().Ladder.IndexOf(c))
                   .Where(i => i is not null).Select(i => i!.Value).ToHashSet();

    public static Dictionary<string, List<SetInfo>> PublishedSets() =>
        Load<Dictionary<string, List<SetInfo>>>("sets.json");

    public static List<ExpansionInfo> Expansions() => Load<List<ExpansionInfo>>("expansions.json");

    public static CardFacts Facts() => _facts ??= new CardFacts(Load<List<CardFact>>("cards.v5.json"));
}
