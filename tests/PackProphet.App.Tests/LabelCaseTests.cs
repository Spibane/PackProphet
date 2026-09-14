namespace PackProphet.App.Tests;

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Bunit;

/// <summary>
/// One rule for how a piece of UI text is capitalised, checked over the markup of every page and
/// component at once.
///
/// A string that ends in a full stop, a question mark or an ellipsis is a sentence, and sentences
/// are written as sentences. Everything else is a label — a heading, a button, a column, an option
/// — and a label is capitalised like a title.
///
/// Written as a scan rather than left to review because casing is the one kind of inconsistency a
/// reviewer stops seeing. Three passes over this app settled on a convention and three times the
/// next page written drifted back: the same button said "Log 3 Anyway" on one branch and "log 3
/// anyway" on the other, and a column read "Cards Wanted" beside one reading "Chance of a hit".
/// Nothing about either is wrong enough to notice on the page it is on. They are only wrong next
/// to each other, which is exactly what no one is ever looking at.
/// </summary>
public class LabelCaseTests : AppHost
{
    /// <summary>
    /// Lowercase inside a title: articles, the seven coordinating conjunctions, and the short
    /// prepositions. Never first or last, where every style capitalises regardless.
    /// </summary>
    private static readonly HashSet<string> Small = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "as", "at", "but", "by", "for", "from", "in", "into", "nor", "of",
        "off", "on", "onto", "or", "over", "per", "so", "the", "to", "up", "via", "with", "yet",
    };

    /// <summary>
    /// Words that are lowercase because the game prints them that way, not because anyone chose
    /// sentence case. Exactly one, measured rather than guessed: "ex" appears in 1,301 card names
    /// in the shipped snapshot and in set names like "Deluxe Pack: ex". Every other lowercase word
    /// in that data ("of", "for", "the", "a", "and", "to", "in", "into") is already small.
    /// </summary>
    private static readonly HashSet<string> Printed = new(StringComparer.Ordinal) { "ex" };

    /// <summary>
    /// The elements whose text is a label by definition. Deliberately not every element that can
    /// hold text: a paragraph is prose, and a span is whatever its page decided it was.
    /// </summary>
    private static readonly string[] Labels =
        ["h1", "h2", "h3", "h4", "h5", "h6", "th", "legend", "option", "summary", "label", "button"];

    private static IEnumerable<(string File, string Text)> Markup()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "markup");

        foreach (var file in Directory.GetFiles(root, "*.razor.txt", SearchOption.AllDirectories))
            yield return (Path.GetFileName(file), File.ReadAllText(file));
    }

    /// <summary>
    /// The markup a rule can be read off: the razor comments and the @code block dropped, since
    /// both are full of prose that is not addressed to anyone using the app.
    /// </summary>
    private static string Visible(string text)
    {
        var code = text.IndexOf("\n@code", StringComparison.Ordinal);
        if (code > 0) text = text[..code];

        return Regex.Replace(text, @"@\*.*?\*@", "", RegexOptions.Singleline);
    }

    /// <summary>
    /// Every label in one file, as far as it can be read.
    ///
    /// A label is often a title with something after it -- a description in its own span, a count,
    /// a card's name -- so what is checked is the words up to the first element or expression,
    /// which is the part written by hand. Where the text was cut short that way, `Whole` is false
    /// and the last word is not treated as last: "Trade for" ends in a preposition only because
    /// the card's name comes next.
    /// </summary>
    private static IEnumerable<(string Text, bool Whole)> LabelsIn(string markup)
    {
        foreach (var tag in Labels)
        {
            foreach (Match m in Regex.Matches(markup, $@"<{tag}\b([^>]*)>(.*?)</{tag}>",
                                              RegexOptions.Singleline))
            {
                // An open tag carrying an expression is usually a lambda, whose own `>` ends the
                // match early and leaves the rest of the attribute looking like text.
                if (m.Groups[1].Value.Contains('@')) continue;

                var inner = m.Groups[2].Value;
                var cut = inner.IndexOfAny(['<', '@']);
                var lead = cut < 0 ? inner : inner[..cut];

                // Entities are drawn glyphs -- an arrow, a multiplication sign -- and their
                // spelling is not a word anyone reads.
                var text = Regex.Replace(lead, @"&[a-zA-Z]+;|&#\d+;", " ");
                text = Regex.Replace(text, @"\s+", " ").Trim();

                if (text.Length > 0) yield return (text, cut < 0);
            }
        }
    }

    /// <summary>The words in a label that a title would have capitalised and this one did not.</summary>
    private static IReadOnlyList<string> NotTitleCased(string label, bool whole)
    {
        // A sentence, and sentences keep sentence case. A colon is not an ending: "Skip: better
        // offers come along" is two labels with a colon between them, not a sentence.
        if (whole && ".?!\u2026".Contains(label[^1])) return [];

        var words = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var last = whole ? words.Length - 1 : int.MaxValue;
        var wrong = new List<string>();

        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i].Trim('(', ')', ',', '"', '\'', '\u2019', ';');

            if (word.Length == 0 || !char.IsLetter(word[0]) || !char.IsLower(word[0])) continue;
            if (i > 0 && i < last && Small.Contains(word.ToLowerInvariant())) continue;

            wrong.Add(word);
        }

        return wrong;
    }

    [Fact]
    public void The_markup_is_there_so_a_pass_below_means_something()
    {
        var labels = Markup().SelectMany(m => LabelsIn(Visible(m.Text))).ToArray();

        // A scan that found nothing would pass by scanning nothing, and the skips above are broad
        // enough that a change to how razor is written could quietly empty it.
        Assert.True(labels.Length > 120,
            $"only {labels.Length} labels were found to check, which is too few to be the app");
    }

    [Fact]
    public void A_label_that_is_not_a_sentence_is_capitalised_like_a_title()
    {
        var offenders = new List<string>();

        foreach (var (file, text) in Markup())
            foreach (var (label, whole) in LabelsIn(Visible(text)))
                if (NotTitleCased(label, whole) is { Count: > 0 } wrong)
                    offenders.Add($"{file}: \"{label}\" ({string.Join(", ", wrong)})");

        Assert.True(offenders.Count == 0,
            "UI text with no full stop at the end of it is a label, and a label is capitalised "
            + "like a title:\n  " + string.Join("\n  ", offenders));
    }

    [Theory]
    [MemberData(nameof(TwoBarRuleTests.ParameterlessPages), MemberType = typeof(TwoBarRuleTests))]
    public async Task A_label_the_page_assembles_at_run_time_is_capitalised_the_same_way(string typeName)
    {
        // The scan above reads the markup, so it only sees words that were typed there. A label
        // built in the @code block is invisible to it -- and that is where the last one hid: the
        // grid's mode button is `$"{Gesture} off"`, which rendered as "tap off" for as long as
        // nobody looked at a phone.
        //
        // So the same rule is applied a second time to what a page actually renders. It costs a
        // render per page and catches the composed case, which is the half that gets missed.
        await ReadyAsync();

        var type = typeof(PackProphet.Services.AppSession).Assembly.GetType(typeName)!;
        var page = RenderComponent<DynamicComponent>(p => p.Add(c => c.Type, type));

        var offenders = new List<string>();

        foreach (var tag in Labels)
            foreach (var el in page.FindAll(tag))
            {
                var text = Regex.Replace(el.TextContent, @"\s+", " ").Trim();
                if (text.Length == 0) continue;

                var wrong = NotTitleCased(text, whole: true)
                    .Where(w => !Printed.Contains(w))
                    .ToArray();

                if (wrong.Length > 0)
                    offenders.Add($"<{tag}> \"{text}\" ({string.Join(", ", wrong)})");
            }

        Assert.True(offenders.Count == 0,
            $"{type.Name} renders a label in sentence case:\n  "
            + string.Join("\n  ", offenders.Distinct()));
    }
}
