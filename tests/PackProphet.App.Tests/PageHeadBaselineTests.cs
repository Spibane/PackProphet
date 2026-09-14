namespace PackProphet.App.Tests;

using System.Text.RegularExpressions;

/// <summary>
/// The page bar's title and subtitle share a baseline, and the correction that makes them is tied
/// to the two font sizes rather than restating them.
///
/// The bar centres its children, and centring is done on boxes: for a single-line item the text
/// baseline lands at the line's centre plus fontSize x (ascent - descent) / 2, so the baseline is
/// a function of the font SIZE and two items at different sizes cannot share one. Measured at
/// 1.13px on every page in the app, which is what "the subtitles are higher than the title" was.
/// app.css corrects it by the difference in size; see the comment there for why line-height,
/// baseline alignment and a wrapper element all fail to.
///
/// What this holds is the one way the correction can rot: it is
/// calc((var(--title-fs) - var(--subtitle-fs)) * .36), so if either size were written as a literal
/// somewhere the calc could no longer see it, changing a size would leave the correction computing
/// the difference between two numbers that are no longer the ones on screen. Nothing would break
/// and nothing would look obviously wrong -- the baselines would just drift apart again by however
/// much the size moved, which is exactly the class of defect nobody spots by reading.
/// </summary>
public class PageHeadBaselineTests
{
    private static string Css() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "app.css"));

    /// <summary>
    /// The declarations of every rule carrying exactly this selector, joined.
    ///
    /// Comments go first, as they do in ContrastTests' reader and for the same reason: a rule's
    /// selector is whatever precedes its brace, so a comment above it is read as part of the
    /// selector and the rule is never found. This file's own comment is thirty lines long.
    /// </summary>
    private static string Bodies(string css, string selector)
    {
        var clean = Regex.Replace(css, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        var found = new List<string>();

        foreach (Match m in Regex.Matches(clean, @"([^{}]+)\{([^{}]*)\}"))
        {
            var selectors = m.Groups[1].Value
                .Split(',')
                .Select(s => Regex.Replace(s.Trim(), @"\s+", " "));

            if (selectors.Contains(selector, StringComparer.Ordinal))
                found.Add(m.Groups[2].Value);
        }

        Assert.True(found.Count > 0, $"no rule in app.css has the selector `{selector}`");
        return string.Join('\n', found);
    }

    [Fact]
    public void The_bar_names_its_two_font_sizes_as_values()
    {
        var head = Bodies(Css(), ".page-head");

        Assert.Matches(@"--title-fs:\s*[\d.]+rem", head);
        Assert.Matches(@"--subtitle-fs:\s*[\d.]+rem", head);
    }

    [Theory]
    [InlineData(".page-head .title", "--title-fs")]
    [InlineData(".page-head .subtitle", "--subtitle-fs")]
    public void Each_reads_its_size_from_the_value_rather_than_a_literal(string selector, string property)
    {
        var body = Bodies(Css(), selector);

        Assert.Matches($@"font-size:\s*var\({property}\)", body);
    }

    [Fact]
    public void The_correction_is_the_difference_between_the_two()
    {
        // Whitespace-insensitive, since this is a formatting choice and not the claim.
        var css = Regex.Replace(Css(), @"\s+", "");

        Assert.Contains("inset-block-start:calc((var(--title-fs)-var(--subtitle-fs))*", css,
                        StringComparison.Ordinal);
    }
}
