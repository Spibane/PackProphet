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
    // The rows that lay a rarity out on a fixed CSS-grid track. Four diamonds are the widest a
    // rarity gets, and an undersized track does not overflow — the row's own ellipsis rule
    // truncates it, so a 4-diamond card silently renders as a 2-diamond one.
    //
    // chase-row is deliberately absent: it is a real table now, and a table column sizes itself to
    // its content, so there is no track to get wrong.
    [InlineData(".card-line", ".c-rr")]
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

    /// <summary>
    /// Utilities that pad or inset a block by a flat amount. Fine anywhere nested; wrong on
    /// anything sitting directly against the screen edge, because none of them clear a cutout.
    /// </summary>
    private static readonly Regex FlatEdgePadding =
        new(@"class=""[^""]*\b(p-[345]|px-[345]|m-[345]|mx-[345])\b", RegexOptions.Compiled);

    public static TheoryData<string> PageFiles()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "pages"), "*.razor.txt"))
            data.Add(f);
        return data;
    }

    [Theory]
    [MemberData(nameof(PageFiles))]
    public void No_page_pads_itself_to_the_screen_edge_by_hand(string file)
    {
        // Padding below the bars belongs to PageShell, which carries env(safe-area-inset-*) so a
        // block clears a display cutout. Six pages instead reached for px-3 or p-3, which is a
        // flat 1rem: in landscape on a notched phone their content sat under the cutout while the
        // bars above it -- fixed earlier, in the same place -- did not.
        //
        // The check is on the page rather than in the browser because it is invisible on a
        // desktop, where every inset resolves to zero. That is how it was missed the first time.
        var markup = File.ReadAllText(file);
        var name = Path.GetFileName(file);

        Assert.False(FlatEdgePadding.IsMatch(markup),
            $"{name} pads a block with a flat utility. Wrap it in <PageShell> instead, or if it " +
            "is genuinely nested inside one already, use a spacing class that is not an edge inset.");
    }

    [Fact]
    public void No_table_cell_is_given_a_display_that_takes_it_out_of_its_row()
    {
        // A <td> set to flex or inline-flex stops being a table cell, so it drops out of the row's
        // height calculation -- and because a cell draws its own bottom border, the row line then
        // sits high in that column and level in every other. On the chase list two cells came out at
        // 25.8px and 34.6px in a 46.6px row, which read as a table with ragged rules.
        //
        // The fix is always the same: leave the cell a cell and put the flex on a wrapper inside
        // it. This catches the tempting version, which looks like it should work.
        var css = WithoutComments(Css);

        var offenders = new List<string>();
        foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}"))
        {
            var selector = Regex.Replace(rule.Groups[1].Value, @"\s+", " ").Trim();
            var body = rule.Groups[2].Value;

            if (!Regex.IsMatch(body, @"display:\s*(inline-)?flex|display:\s*(inline-)?grid")) continue;

            // Only the cell itself matters. A selector ending in a descendant of the cell is the
            // correct shape and must keep passing.
            foreach (var part in selector.Split(','))
            {
                var last = part.Trim().Split(' ').LastOrDefault()?.Trim() ?? "";
                if (Regex.IsMatch(last, @"^(td|th)([.:\[#].*)?$"))
                    offenders.Add($"{part.Trim()} sets a flex or grid display");
            }
        }

        Assert.True(offenders.Count == 0,
            "These put a table cell outside its own row, so its bottom border stops lining up " +
            "with the rest: " + string.Join("; ", offenders));
    }

    [Fact]
    public void Every_stepper_size_clears_the_minimum_target()
    {
        // WCAG 2.5.8 asks for 24x24 CSS px, and the -/+ buttons were 23 tall for as long as they
        // were spelled `btn btn-sm py-0` by hand in each of four places. They are one class now,
        // sized by one variable, so the only way back to a failing target is a caller lowering the
        // variable -- which reads as a harmless bit of tightening and is not.
        var declarations = Regex.Matches(WithoutComments(Css), @"--step-size:\s*([\d.]+)rem");
        Assert.True(declarations.Count > 0, "no --step-size declared; the shared stepper is gone");

        foreach (Match d in declarations)
        {
            var px = double.Parse(d.Groups[1].Value) * 16;
            Assert.True(px >= 24,
                $"--step-size: {d.Groups[1].Value}rem is {px}px, under the 24px floor in " +
                "WCAG 2.5.8. The gap between two steppers is far under 24px too, so the " +
                "spacing exception does not cover it.");
        }
    }

    /// <summary>
    /// Every file whose markup can carry a grid template: the pages, plus the grid component
    /// that lays a row of tiles out from a column count the user picked.
    /// </summary>
    public static TheoryData<string> MarkupFiles()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "pages"), "*.razor.txt"))
            data.Add(f);
        data.Add(Path.Combine(AppContext.BaseDirectory, "CardGrid.razor.txt"));
        return data;
    }

    /// <summary>
    /// A bare 1fr means minmax(AUTO, 1fr), and an auto minimum is the track's min-content width —
    /// so the track never shrinks below the longest unbreakable thing in it, however narrow the
    /// screen. On a grid whose column count the user chose, that is not a hypothetical: six pack
    /// tiles at 375px needed about 460px, so the last column was cut off AND the browser widened
    /// its layout viewport to fit, which pushed the fixed bottom bar off the bottom of the screen.
    /// One overflowing grid, two symptoms that look unrelated.
    ///
    /// minmax(0, 1fr) is the same layout wherever there is room and the only one that degrades. It
    /// is checked rather than remembered because the failure is invisible at a desk, which is where
    /// the column count gets changed.
    /// </summary>
    private static readonly Regex BareFrTrack = new(@"repeat\(\s*[^,()]+,\s*1fr\s*\)", RegexOptions.Compiled);

    [Theory]
    [MemberData(nameof(MarkupFiles))]
    public void No_markup_repeats_a_bare_1fr_track(string file)
    {
        var markup = File.ReadAllText(file);
        var name = Path.GetFileName(file);

        Assert.False(BareFrTrack.IsMatch(markup),
            $"{name} lays a grid out with repeat(n, 1fr). That is minmax(auto, 1fr), so the tracks " +
            "cannot shrink below their content and the grid overflows the screen instead of " +
            "fitting it. Use minmax(0, 1fr).");
    }

    [Fact]
    public void No_stylesheet_rule_repeats_a_bare_1fr_track()
    {
        var offenders = TrackDeclarations()
            .Where(d => BareFrTrack.IsMatch(d.Tracks))
            .Select(d => $"{d.Selector} {{ grid-template-columns: {d.Tracks} }}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "These cannot shrink below their content, so they widen the page rather than fit it. " +
            "Use minmax(0, 1fr): " + string.Join("; ", offenders));
    }

    private static int Height(string pattern)
    {
        var m = Regex.Match(WithoutComments(Css), pattern, RegexOptions.Singleline);
        Assert.True(m.Success, $"no height matched {pattern}");
        return int.Parse(m.Groups[1].Value);
    }
}
