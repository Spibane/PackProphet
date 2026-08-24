namespace PackProphet.App.Tests;

using System.Text.RegularExpressions;

/// <summary>
/// Every rendered page, checked for attribute names a browser will refuse.
///
/// A Razor comment placed inside an attribute list is not treated as a comment: Razor emits its
/// text as an attribute name, and the browser throws "InvalidCharacterError: Element.setAttribute:
/// Invalid attribute name", which takes down the whole component.
///
/// Nothing else catches it. It compiles, and bUnit renders it happily, because its DOM is a parser
/// rather than a browser and does not enforce the name grammar. Only a real browser complains, at
/// runtime, with a message naming no element or file.
/// </summary>
public class MarkupValidityTests : AppHost
{
    /// <summary>
    /// HTML's attribute-name grammar. Razor's own directives never survive into markup as names of
    /// this shape, so anything failing this came from a comment or an interpolation gone wrong.
    /// </summary>
    private static readonly Regex Name = new(@"^[A-Za-z_:][-A-Za-z0-9_:.]*$", RegexOptions.Compiled);

    /// <summary>
    /// Walks the markup and yields every attribute name, including valueless ones.
    ///
    /// A scanner rather than a regex: Razor emits a misplaced comment as a run of bare, valueless
    /// tokens — <c>@*</c>, then one per word — so a pattern looking for name="value" pairs misses
    /// it. Quoted values are skipped properly too, since a card's tooltip can contain newlines and
    /// angle brackets.
    /// </summary>
    private static IEnumerable<string> AttributeNames(string markup)
    {
        var i = 0;
        while (i < markup.Length)
        {
            if (markup[i] != '<') { i++; continue; }
            i++;

            // Closing tags, comments and doctypes carry no attributes.
            if (i < markup.Length && (markup[i] == '/' || markup[i] == '!'))
            {
                while (i < markup.Length && markup[i] != '>') i++;
                continue;
            }

            while (i < markup.Length && !char.IsWhiteSpace(markup[i])
                   && markup[i] != '>' && markup[i] != '/') i++;

            while (i < markup.Length && markup[i] != '>')
            {
                if (char.IsWhiteSpace(markup[i]) || markup[i] == '/') { i++; continue; }

                var start = i;
                while (i < markup.Length && !char.IsWhiteSpace(markup[i])
                       && markup[i] != '=' && markup[i] != '>') i++;

                yield return markup[start..i];

                if (i < markup.Length && markup[i] == '=')
                {
                    i++;
                    if (i < markup.Length && (markup[i] == '"' || markup[i] == '\''))
                    {
                        var quote = markup[i++];
                        while (i < markup.Length && markup[i] != quote) i++;
                        i++;
                    }
                    else
                    {
                        while (i < markup.Length && !char.IsWhiteSpace(markup[i])
                               && markup[i] != '>') i++;
                    }
                }
            }
            i++;
        }
    }

    private void AssertValid(string markup, string what)
    {
        var bad = AttributeNames(markup).Where(n => !Name.IsMatch(n)).Distinct().Take(8).ToArray();

        Assert.True(bad.Length == 0,
            $"{what} renders attribute name(s) a browser will refuse: " +
            string.Join(", ", bad.Select(b => $"'{b}'")));
    }

    public static TheoryData<string> Pages => EveryPageRendersTests.ParameterlessPages;

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Every_page_renders_only_valid_attribute_names(string typeName)
    {
        await ReadyAsync();

        var type = typeof(PackProphet.Services.AppSession).Assembly.GetType(typeName)!;
        var markup = RenderComponent<Microsoft.AspNetCore.Components.DynamicComponent>(
            p => p.Add(c => c.Type, type)).Markup;

        AssertValid(markup, typeName);
    }

    [Fact]
    public async Task The_card_grid_and_its_tiles_render_valid_attribute_names()
    {
        // Called out separately because the tile is where it happened: the grid is the one place
        // with enough attributes on one element for a comment to be tucked in among them.
        await ReadyAsync();

        var cards = Session.Index.All.DistinctBy(c => c.OwnershipKey).Take(12).ToArray();

        foreach (var listView in new[] { false, true })
        {
            var grid = RenderComponent<PackProphet.Components.CardGrid>(p =>
            {
                p.Add(g => g.Cards, cards);
                p.Add(g => g.CountOf, _ => 1);
                p.Add(g => g.ListView, listView);
            });

            AssertValid(grid.Markup, $"CardGrid(ListView={listView})");
        }
    }

    [Fact]
    public void The_check_would_actually_notice()
    {
        // A guard on the guard. If the pattern ever stops matching what Razor emits for a misplaced
        // comment, every test above would pass while checking nothing.
        AssertValid("""<div class="ok" data-x="1" hidden disabled blazor:onclick:preventDefault>""",
                    "clean markup");

        // Exactly what Razor emits for a comment inside an attribute list: bare tokens, no values.
        var ex = Assert.Throws<Xunit.Sdk.TrueException>(() =>
            AssertValid("""<div class="x" @* a stray comment *@ id="y">""", "dirty markup"));

        Assert.Contains("@*", ex.Message);
    }
}
