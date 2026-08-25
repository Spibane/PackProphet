namespace PackProphet.App.Tests;

using System.Text.RegularExpressions;

/// <summary>
/// Layout facts that live in more than one place and must agree.
///
/// Every bug these guard against is silent: nothing throws, nothing fails to render, the page just
/// looks subtly wrong. A rarity reads as a lower rarity, a column drifts out of line with its
/// header, or a list jumps while it scrolls. They were all found by eye, repeatedly, and the ones
/// about column tracks were each found more than once — a value declared in four places was fixed
/// in the two that had been looked at.
/// </summary>
public class LayoutInvariantTests
{
    private static readonly string Css =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "app.css"));

    private static readonly string Grid =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CardGrid.razor.txt"));

    private static string WithoutComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    /// <summary>
    /// Every grid-template-columns in the file, as (selector, declaration). Media conditions are
    /// folded away: what matters is that a selector's variants agree, not which width each covers.
    /// </summary>
    private static List<(string Selector, string Tracks)> TrackDeclarations()
    {
        var css = WithoutComments(Css);
        var found = new List<(string, string)>();
        var stack = new List<string>();
        var buf = new System.Text.StringBuilder();

        for (var i = 0; i < css.Length; i++)
        {
            var c = css[i];
            if (c == '{') { stack.Add(buf.ToString().Trim()); buf.Clear(); }
            else if (c == '}') { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); buf.Clear(); }
            else if (c == ';') buf.Clear();
            else
            {
                buf.Append(c);
                if (buf.ToString().EndsWith("grid-template-columns", StringComparison.Ordinal))
                {
                    var end = css.IndexOf(';', i);
                    var value = css[(i + 1)..end].TrimStart(':', ' ', '\n', '\r', '\t');
                    var selector = stack.Count > 0 ? stack[^1] : "";
                    found.Add((Regex.Replace(selector, @"\s+", " ").Trim(),
                               Regex.Replace(value, @"\s+", " ").Trim()));
                    i = end;
                    buf.Clear();
                }
            }
        }
        return found;
    }

    [Theory]
    // The rows that show a card's rarity as glyphs. Four diamonds are the widest a rarity gets, and
    // an undersized track does not overflow — the row's own ellipsis rule truncates it, so a
    // 4-diamond card silently renders as a 2-diamond one.
    [InlineData(".card-line", ".c-rr")]
    [InlineData(".wish-row", ".rt")]
    [InlineData(".supplied-row", ".rt")]
    public void Every_variant_sizes_its_rarity_column_from_the_shared_token(string row, string cell)
    {
        var variants = TrackDeclarations()
            .Where(d => Regex.IsMatch(d.Selector, $@"(^|[\s,]){Regex.Escape(row)}\b")
                        && !d.Selector.Contains(':'))
            .ToList();

        Assert.True(variants.Count > 0, $"no grid-template-columns found for {row}");

        foreach (var (selector, tracks) in variants)
        {
            // A variant may drop the column entirely instead of sizing it; that is equally safe.
            var dropsIt = Regex.IsMatch(WithoutComments(Css),
                $@"{Regex.Escape(row)}\s+{Regex.Escape(cell)}\s*\{{[^}}]*display:\s*none");

            Assert.True(tracks.Contains("var(--rarity-col)") || dropsIt,
                $"{selector} sizes its rarity column with a literal. Four diamonds do not fit a " +
                $"guessed width, and this is the declaration that gets missed when the others are " +
                $"fixed — use var(--rarity-col), or hide {cell} at this width.");
        }
    }

    [Fact]
    public void The_card_line_variants_agree_on_which_tokens_size_them()
    {
        var variants = TrackDeclarations()
            .Where(d => Regex.IsMatch(d.Selector, @"(^|[\s,])\.card-line\b") && !d.Selector.Contains(':'))
            .ToList();

        Assert.True(variants.Count >= 4, "card-line should still have a template per breakpoint");

        // Each variant drops columns, so they do not use the same NUMBER of tracks — but a column
        // that survives must be sized the same way everywhere, or one breakpoint clips where the
        // others do not.
        foreach (var (selector, tracks) in variants)
        {
            foreach (var token in new[] { "--art-w", "--rarity-col", "--pick-col" })
            {
                Assert.True(tracks.Contains($"var({token})"),
                    $"{selector} does not size itself with {token}. Every card-line template must, " +
                    $"or changing that column fixes some widths and not others.");
            }
        }
    }

    [Fact]
    public void Virtualize_item_size_matches_the_row_height_it_renders()
    {
        // Virtualize is told a fixed row height up front. If the stylesheet and that number
        // disagree the scrollbar is mis-sized and the list jumps while scrolling, which looks like
        // a rendering bug rather than an arithmetic one.
        var compact = Height(@"\.card-line\s*\{[^}]*?height:\s*(\d+)px");
        var roomy = Height(@"\.card-line\.roomy:not\(\.head\)\s*\{[^}]*?height:\s*(\d+)px");

        var itemSize = Regex.Match(Grid, @"ItemSize=""@\(Roomy \? (\d+)f : (\d+)f\)""");
        Assert.True(itemSize.Success, "could not find the list's ItemSize in CardGrid.razor");

        var declaredRoomy = int.Parse(itemSize.Groups[1].Value);
        var declaredCompact = int.Parse(itemSize.Groups[2].Value);

        // The border is outside the declared height, so a row occupies height + 1.
        Assert.Equal(compact + 1, declaredCompact);
        Assert.Equal(roomy + 1, declaredRoomy);
    }

    private static int Height(string pattern)
    {
        var m = Regex.Match(WithoutComments(Css), pattern, RegexOptions.Singleline);
        Assert.True(m.Success, $"no height matched {pattern}");
        return int.Parse(m.Groups[1].Value);
    }
}
