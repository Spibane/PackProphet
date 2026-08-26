namespace PackProphet.App.Tests;

using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Components;

/// <summary>
/// Accessibility rules that hold for every page, checked against the rendered DOM rather than the
/// source, so a control that gets its name from a component two levels up still counts.
///
/// Written as sweeps over the reflection-discovered page list for the same reason the render tests
/// are: a page added later is covered without anyone remembering to come back here.
/// </summary>
public class AccessibilityTests : AppHost
{
    public static TheoryData<string> Pages => EveryPageRendersTests.ParameterlessPages;

    private IRenderedFragment Page(string typeName)
    {
        var type = typeof(PackProphet.Services.AppSession).Assembly.GetType(typeName)!;
        return RenderComponent<DynamicComponent>(p => p.Add(c => c.Type, type));
    }

    private static bool HasName(IElement e) =>
        !string.IsNullOrWhiteSpace(e.GetAttribute("aria-label"))
        || !string.IsNullOrWhiteSpace(e.GetAttribute("aria-labelledby"))
        || !string.IsNullOrWhiteSpace(e.GetAttribute("title"));

    /// <summary>
    /// A control is named by an aria attribute, by a label pointing at its id, or by a label
    /// wrapping it. A placeholder is deliberately not accepted: it disappears the moment anyone
    /// types, which is also the moment they most need to be told what the field was.
    /// </summary>
    private static bool IsLabelled(IElement e, IRenderedFragment page)
    {
        if (HasName(e)) return true;

        var id = e.GetAttribute("id");
        if (!string.IsNullOrWhiteSpace(id) && page.FindAll($"label[for='{id}']").Count > 0) return true;

        return e.Closest("label") is not null;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Every_form_control_has_a_name(string typeName)
    {
        await ReadyAsync();
        var page = Page(typeName);

        var unnamed = page.FindAll("input,select,textarea")
            .Where(e => e.GetAttribute("type") != "hidden")
            .Where(e => !IsLabelled(e, page))
            .Select(Describe)
            .ToArray();

        Assert.True(unnamed.Length == 0,
            $"{typeName} has {unnamed.Length} control(s) with no accessible name:\n  "
            + string.Join("\n  ", unnamed));
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Every_button_and_link_has_a_name(string typeName)
    {
        await ReadyAsync();
        var page = Page(typeName);

        var unnamed = page.FindAll("button,a")
            .Where(e => string.IsNullOrWhiteSpace(e.TextContent) && !HasName(e))
            .Select(Describe)
            .ToArray();

        Assert.True(unnamed.Length == 0,
            $"{typeName} has {unnamed.Length} control(s) with neither text nor a label:\n  "
            + string.Join("\n  ", unnamed));
    }

    /// <summary>
    /// A picture that carries data has to say what the data is. `role="img"` hides an element's
    /// children from assistive technology, so whatever is inside it stops existing and the label
    /// is the only thing left.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Every_image_role_has_a_name(string typeName)
    {
        await ReadyAsync();
        var page = Page(typeName);

        var unnamed = page.FindAll("[role=img],img")
            .Where(e => e.LocalName == "img"
                ? e.GetAttribute("alt") is null
                : !HasName(e))
            .Select(Describe)
            .ToArray();

        Assert.True(unnamed.Length == 0,
            $"{typeName} has {unnamed.Length} image(s) with no alternative:\n  "
            + string.Join("\n  ", unnamed));
    }

    /// <summary>
    /// Heading levels may go down freely and up only one at a time. A jump means a screen reader's
    /// outline claims a section sits inside one that was never opened.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Heading_levels_never_skip(string typeName)
    {
        await ReadyAsync();
        var page = Page(typeName);

        var last = 0;
        var jumps = new List<string>();

        foreach (var h in page.FindAll("h1,h2,h3,h4,h5,h6"))
        {
            var level = int.Parse(h.LocalName[1..], CultureInfo.InvariantCulture);
            if (last > 0 && level > last + 1)
                jumps.Add($"h{last} -> h{level} at \"{h.TextContent.Trim()}\"");
            last = level;
        }

        Assert.True(jumps.Count == 0, $"{typeName} skips heading levels:\n  " + string.Join("\n  ", jumps));
    }

    private static string Describe(IElement e)
    {
        var html = e.OuterHtml.Replace('\n', ' ');
        return html.Length > 160 ? html[..160] : html;
    }
}
