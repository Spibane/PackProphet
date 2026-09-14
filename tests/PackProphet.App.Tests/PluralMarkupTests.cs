namespace PackProphet.App.Tests;

using System.Text.RegularExpressions;

/// <summary>
/// No page writes "card(s)".
///
/// It is the shortcut a count reaches for when the plural is not known at the time of writing, and
/// twenty-five of them had accumulated across the app — beside a dozen places that had done it
/// properly, so the same figure read "1 card" on one screen and "1 card(s)" on the next. Fmt.S
/// takes the count and picks the word.
/// </summary>
public class PluralMarkupTests
{
    private static IEnumerable<(string File, string Text)> Markup()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "markup");

        foreach (var file in Directory.GetFiles(root, "*.razor.txt", SearchOption.AllDirectories))
            yield return (Path.GetFileName(file), File.ReadAllText(file));
    }

    [Fact]
    public void A_plural_is_chosen_rather_than_left_in_brackets()
    {
        var offenders = new List<string>();

        foreach (var (file, text) in Markup())
        {
            // The markup only: `@code` holds lambdas like `Where(s => ...)`, whose parameter is an
            // `s` in brackets and nothing to do with a plural.
            var code = text.IndexOf("\n@code", StringComparison.Ordinal);
            var markup = code > 0 ? text[..code] : text;

            foreach (Match m in Regex.Matches(markup, @"\w+\(s\)"))
                offenders.Add($"{file}: \"{m.Value}\"");
        }

        Assert.True(offenders.Count == 0,
            "a count needs the right word beside it, not both: use Fmt.S(n, \"card\").\n  "
            + string.Join("\n  ", offenders.Distinct()));
    }
}
