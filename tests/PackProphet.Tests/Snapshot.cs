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

    /// <summary>
    /// The rate table as it stood before <paramref name="set"/>'s rates were published.
    ///
    /// For the tests about a set with no rates. They used to name whichever set was unpriced at
    /// the time, and each refresh that priced it left them asserting something else: B4 gained
    /// rates in 2.11.0, and a test borrowing rates "for a set that had none" went on passing
    /// because B4's own rates were never overwritten. Withholding one set's rates keeps them about
    /// the behaviour, and it is exactly the table the app ran on the week before.
    /// </summary>
    public static PullRates RatesWithout(string set) => new(
        Rates().ModelledSets
               .Where(s => !string.Equals(s, set, StringComparison.OrdinalIgnoreCase))
               .ToDictionary(s => s, s => Rates().Published(s).ToDictionary()));

    // ---- what upstream has not caught up on ------------------------------------------

    /// <summary>
    /// The most recently released numbered set: the one card data has and everything else may not
    /// have yet.
    ///
    /// Worked out rather than named, because it moves on every refresh. Card data ships the day a
    /// set is announced, and detail, pack art and the odd card's artwork follow over the next
    /// weeks, so the tests stating "every card has X" all need the same allowance for the same
    /// set, and it has to be whichever set that is.
    /// </summary>
    public static string NewestSet()
    {
        var sets = new SetCatalog(PublishedSets(), Index().BySet.Keys);
        return Index().BySet.Keys
            .Where(s => !CardIndex.IsPromoSet(s))
            .OrderByDescending(s => sets.ReleaseDateOf(s) ?? DateOnly.MinValue)
            .ThenByDescending(s => sets.SortKey(s), StringComparer.Ordinal)
            .First();
    }

    private static HashSet<string>? _awaitingDetail;

    /// <summary>
    /// Printings with no detail that no test should hold against the snapshot, by card key.
    ///
    /// Two shapes, and nothing else. The newest set when the detail table has none of it, which is
    /// what a set looks like for its first few weeks: 2.11.0 brought B4b's 429 cards and none of
    /// its attacks. And the highest-numbered run of a promo set, because promos are published a
    /// handful at a time on the end of the list and their detail follows the same way: PROMO-B
    /// 95-103 arrived together and undescribed.
    ///
    /// A shape rather than a list of keys, so it stays right as the frontier moves, and a narrow
    /// one, so the tests still catch what they were written for. A numbered set short of SOME of
    /// its detail is not excused, and nor is a promo missing from the middle of the list: neither
    /// is upstream running late, and both are a join that broke.
    /// </summary>
    public static IReadOnlySet<string> AwaitingDetail()
    {
        if (_awaitingDetail is not null) return _awaitingDetail;

        var facts = Facts();
        var awaiting = new HashSet<string>(StringComparer.Ordinal);

        var newest = Index().BySet[NewestSet()];
        if (newest.All(c => facts.ForPrinting(c) is null))
            awaiting.UnionWith(newest.Select(c => c.Key));

        foreach (var (set, cards) in Index().BySet.Where(kv => CardIndex.IsPromoSet(kv.Key)))
            awaiting.UnionWith(cards.OrderByDescending(c => c.Number)
                                    .TakeWhile(c => facts.ForPrinting(c) is null)
                                    .Select(c => c.Key));

        return _awaitingDetail = awaiting;
    }
}
