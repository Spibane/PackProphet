namespace PackProphet.App.Tests;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// The text colours this project overrides, checked against the surfaces they sit on.
///
/// Read out of app.css and recomputed rather than compared to a remembered number, so editing a
/// colour re-runs the arithmetic instead of quietly invalidating a comment. The values themselves
/// were measured in a browser with getComputedStyle across every page in both themes; this test
/// is what stops them drifting back.
///
/// It cannot see anything Bootstrap ships unchanged, and it cannot see a colour used somewhere
/// this file does not name. It covers the ones that were found to be under the line and fixed.
/// </summary>
public class ContrastTests
{
    /// <summary>WCAG 2.2 AA for body-sized text. Large text gets 3:1; nothing here is large.</summary>
    private const double Aa = 4.5;

    /// <summary>
    /// The two surfaces a control sits on in each theme: the raised panel and the page body. The
    /// panel is the harder of the two — it is closer to the text colour — so a value clearing it
    /// clears both, but both are asserted because "the harder one" is a claim that could stop
    /// being true.
    ///
    /// Light comes from this stylesheet's own --bs-tertiary-bg; dark is Bootstrap's, unmodified,
    /// so it is named here rather than parsed.
    /// </summary>
    private static readonly (string Panel, string Body) LightSurfaces = ("#edf0f4", "#ffffff");
    private static readonly (string Panel, string Body) DarkSurfaces = ("#2b3035", "#212529");

    private static string Css() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "app.css"));

    /// <summary>
    /// The value of a custom property in the rule whose selector list contains the given selector.
    ///
    /// The selector is matched as a whole comma-separated entry, not as a substring. Substring
    /// matching finds `[data-bs-theme="dark"]` inside the light rule's own
    /// `:root:not([data-bs-theme="dark"])` and reads the light colour as the dark one — which is
    /// how this test first "failed", reporting a 1.11:1 that did not exist anywhere in the app.
    ///
    /// Every matching rule is searched, since a theme selector legitimately appears more than once
    /// in this stylesheet: one rule sets border colours, another sets this.
    /// </summary>
    private static string Declared(string css, string selector, string property)
    {
        var found = false;

        foreach (var (selectors, body) in Rules(css))
        {
            if (!selectors.Contains(selector, StringComparer.Ordinal)) continue;
            found = true;

            var value = Regex.Match(body, Regex.Escape(property) + @"\s*:\s*(?<value>[^;]+);");
            if (value.Success) return value.Groups["value"].Value.Trim();
        }

        Assert.True(found, $"no rule found whose selector list contains '{selector}'");
        Assert.Fail($"'{property}' not declared in any rule for '{selector}'");
        return "";
    }

    /// <summary>
    /// Top-level rules as (selectors, body). Comments are stripped first, since this stylesheet's
    /// comments contain braces and selector-looking text in their explanations.
    /// </summary>
    private static IEnumerable<(string[] Selectors, string Body)> Rules(string css)
    {
        var clean = Regex.Replace(css, @"/\*.*?\*/", " ", RegexOptions.Singleline);

        foreach (Match rule in Regex.Matches(clean, @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}"))
        {
            var selectors = rule.Groups["sel"].Value
                .Split(',')
                .Select(s => Regex.Replace(s.Trim(), @"\s+", " "))
                .Where(s => s.Length > 0)
                .ToArray();

            yield return (selectors, rule.Groups["body"].Value);
        }
    }

    [Theory]
    // The three outline-button variants the app actually uses, in both themes. Bootstrap draws
    // these in the theme colour on a transparent background, which failed in both.
    [InlineData("[data-bs-theme=\"light\"] .btn-outline-secondary", "--bs-btn-color", false)]
    [InlineData("[data-bs-theme=\"light\"] .btn-outline-danger", "--bs-btn-color", false)]
    [InlineData("[data-bs-theme=\"light\"] .btn-outline-primary", "--bs-btn-color", false)]
    [InlineData("[data-bs-theme=\"dark\"] .btn-outline-secondary", "--bs-btn-color", true)]
    [InlineData("[data-bs-theme=\"dark\"] .btn-outline-danger", "--bs-btn-color", true)]
    [InlineData("[data-bs-theme=\"dark\"] .btn-outline-primary", "--bs-btn-color", true)]
    public void An_overridden_text_colour_clears_the_contrast_floor(
        string selector, string property, bool dark)
    {
        var colour = Parse(Declared(Css(), selector, property));
        var (panel, body) = dark ? DarkSurfaces : LightSurfaces;

        foreach (var surface in new[] { panel, body })
        {
            var ratio = Contrast(colour, Parse(surface));
            Assert.True(ratio >= Aa,
                $"{selector} is {ratio:0.00}:1 on {surface}, under the {Aa}:1 floor");
        }
    }

    /// <summary>
    /// The quietest copy in the app, which Bootstrap ships as the body colour at half alpha —
    /// 3.02:1 in light. Asserted separately because it is a translucent colour, so the check has
    /// to composite it onto each surface first.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void De_emphasised_text_clears_the_contrast_floor(bool dark)
    {
        var selector = dark ? "[data-bs-theme=\"dark\"]" : "[data-bs-theme=\"light\"]";
        var colour = Parse(Declared(Css(), selector, "--bs-tertiary-color"));
        var (panel, body) = dark ? DarkSurfaces : LightSurfaces;

        foreach (var surface in new[] { panel, body })
        {
            var bg = Parse(surface);
            var ratio = Contrast(Composite(colour, bg), bg);

            Assert.True(ratio >= Aa,
                $"--bs-tertiary-color is {ratio:0.00}:1 on {surface} in {(dark ? "dark" : "light")}, "
                + $"under the {Aa}:1 floor");
        }
    }

    /// <summary>
    /// The heart on a card tile, in both states, against the caption it sits on.
    ///
    /// A graphic that carries meaning, not text: it is the only thing on the tile saying "this is on
    /// your want list", so WCAG 1.4.11 asks 3:1 rather than 4.5:1. Two things make it easy to get
    /// wrong and are why this is asserted rather than eyeballed — the caption's surface is the
    /// tertiary background rather than the body, and the idle heart carries an element opacity on
    /// top of an already translucent colour, so the alpha that matters is the product of the two.
    ///
    /// Raw --bs-danger measured 2.94:1 on the dark caption, which is what --pp-want-ink exists for.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_want_heart_clears_the_non_text_floor(bool dark)
    {
        const double NonText = 3.0;

        var surface = Parse(dark ? DarkSurfaces.Panel : LightSurfaces.Panel);
        var theme = dark ? "[data-bs-theme=\"dark\"]" : "[data-bs-theme=\"light\"]";

        // On: the state that means something, so the one that has to be seen.
        var on = Parse(Declared(Css(), theme, "--pp-want-ink"));
        var onRatio = Contrast(on, surface);
        Assert.True(onRatio >= NonText,
            $"the filled heart is {onRatio:0.00}:1 on the caption in {(dark ? "dark" : "light")}, "
            + $"under the {NonText}:1 floor for a meaningful graphic");

        // Idle: --bs-secondary-color, which is the body colour at .75, times the .85 the element
        // itself carries. Bootstrap's own values, named here as the surfaces above are.
        var body = dark ? new Rgba(222, 226, 230) : new Rgba(33, 37, 41);
        var idle = body with { A = 0.75 * 0.85 };
        var idleRatio = Contrast(Composite(idle, surface), surface);

        Assert.True(idleRatio >= NonText,
            $"the empty heart is {idleRatio:0.00}:1 on the caption in {(dark ? "dark" : "light")}");
    }

    /// <summary>
    /// White on the count badge. It sits at exactly 4.50:1 — the AA floor for text this size, which
    /// it passes and could not pass by less. Pinned because the badge got SMALLER when it moved off
    /// the art, so it is squarely small text now, and because a nudge to --bs-primary in either
    /// direction would take it under without anything else in the app noticing.
    /// </summary>
    [Fact]
    public void The_count_badge_clears_the_text_floor()
    {
        // Bootstrap's primary and success, unmodified by this stylesheet — named here like the
        // surfaces, so a change to either is a change to this test.
        foreach (var fill in new[] { "#0d6efd", "#198754" })
        {
            var ratio = Contrast(Parse("#ffffff"), Parse(fill));
            Assert.True(ratio >= Aa, $"white on {fill} is {ratio:0.00}:1, under the {Aa}:1 floor");
        }
    }

    /// <summary>A guard on the guard: the maths has to fail something known to be too faint.</summary>
    [Fact]
    public void The_check_would_actually_notice()
    {
        // Bootstrap's original half-alpha tertiary on the light panel, the value this replaced.
        var faint = new Rgba(33, 37, 41, 0.5);
        var bg = Parse("#edf0f4");

        Assert.True(Contrast(Composite(faint, bg), bg) < Aa);
        Assert.True(Contrast(Parse("#000000"), Parse("#ffffff")) > 20);
    }

    private readonly record struct Rgba(double R, double G, double B, double A = 1);

    private static Rgba Parse(string value)
    {
        var v = value.Trim();

        if (v.StartsWith('#'))
        {
            var h = v[1..];
            int Part(int i) => int.Parse(h.Substring(i, 2), NumberStyles.HexNumber,
                                         CultureInfo.InvariantCulture);
            return new Rgba(Part(0), Part(2), Part(4));
        }

        var m = Regex.Match(v, @"rgba?\(([^)]*)\)");
        Assert.True(m.Success, $"cannot parse colour '{value}'");

        var parts = m.Groups[1].Value.Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();

        return new Rgba(parts[0], parts[1], parts[2], parts.Length > 3 ? parts[3] : 1);
    }

    /// <summary>A translucent colour as it actually lands on a given background.</summary>
    private static Rgba Composite(Rgba fg, Rgba bg) => new(
        fg.R * fg.A + bg.R * (1 - fg.A),
        fg.G * fg.A + bg.G * (1 - fg.A),
        fg.B * fg.A + bg.B * (1 - fg.A));

    private static double Luminance(Rgba c)
    {
        static double Channel(double v)
        {
            v /= 255;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Rgba a, Rgba b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
}
