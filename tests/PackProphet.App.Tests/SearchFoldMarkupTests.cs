namespace PackProphet.App.Tests;

using System.Text.RegularExpressions;

/// <summary>
/// Every search box in the app folds what was typed, and none of them compares a card name raw.
///
/// There are four name searches on four screens — the collection's rail, the pack log's find box,
/// the command palette, and the deck builder's picker through CardSearch — and three of them were
/// a bare <c>Name.Contains(term, OrdinalIgnoreCase)</c>. So "poke ball" found nothing on any of
/// them, because the card is Poké Ball, and "rockets meowth" found nothing because the index
/// writes Team Rocket’s with U+2019.
///
/// The fix is one helper, and the risk is the same as it was before: the next screen with a search
/// box writes the obvious thing. This is what catches that, and it is a scan rather than a review
/// because the obvious thing is obviously fine to read — it is only wrong about the fifty names in
/// 3,879 that are not plain ASCII, and none of those is the card anyone is testing with.
/// </summary>
public class SearchFoldMarkupTests
{
    private static IEnumerable<(string File, string Text)> Markup()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "markup");

        foreach (var file in Directory.GetFiles(root, "*.razor.txt", SearchOption.AllDirectories))
            yield return (Path.GetFileName(file), File.ReadAllText(file));
    }

    [Fact]
    public void No_page_matches_a_card_name_without_folding_it()
    {
        var offenders = new List<string>();

        foreach (var (file, text) in Markup())
            foreach (Match m in Regex.Matches(text, @"Name\.(Contains|StartsWith|IndexOf)\s*\("))
                offenders.Add($"{file}: Name.{m.Groups[1].Value}(");

        Assert.True(offenders.Count == 0,
            "a name search has to fold both sides or it cannot find Poké Ball: use "
            + "SearchKey.Has(name, SearchKey.Fold(term)).\n  "
            + string.Join("\n  ", offenders.Distinct()));
    }

    /// <summary>
    /// The pass above means something only if the scan is reading the files the searches are in.
    /// A path typo, a fixture that stopped being copied, or a rename would otherwise turn it green
    /// by finding nothing at all — which is the failure mode of every test written as an absence.
    /// </summary>
    [Fact]
    public void The_searches_this_is_about_are_in_what_it_read()
    {
        var folding = Markup()
            .Where(m => m.Text.Contains("SearchKey.", StringComparison.Ordinal))
            .Select(m => m.File)
            .ToHashSet(StringComparer.Ordinal);

        // Named as a floor rather than as the whole set, so the next page to fold a search does
        // not have to come back and edit this. The lesson is a recent one: five tests named the
        // newest set rather than the behaviour, and a routine card refresh broke all five.
        Assert.Contains("Collection.razor.txt", folding);
        Assert.Contains("CommandPalette.razor.txt", folding);
        Assert.Contains("LogPack.razor.txt", folding);
    }
}
