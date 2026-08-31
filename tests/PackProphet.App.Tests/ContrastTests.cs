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

    /// <summary>
    /// The same two surfaces for the paper skin, which declares its own page background as well as
    /// its own panel — so both are read out of the stylesheet rather than named here. That also
    /// means a change to either surface re-runs every check below against the new one, which is the
    /// property the slate skin's hardcoded dark pair does not have.
    /// </summary>
    private static (string Panel, string Body) PaperSurfaces(string css, bool dark) =>
        (Declared(css, PaperRule(dark), "--bs-tertiary-bg"),
         Declared(css, PaperRule(dark), "--bs-body-bg"));

    private static string PaperRule(bool dark) =>
        $"[data-pp-skin=\"paper\"][data-bs-theme=\"{(dark ? "dark" : "light")}\"]";

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
    ///
    /// The property name is matched on a NAME boundary, not as a bare substring. Every query here
    /// used to be a custom property, which cannot be the tail of another declaration in practice,
    /// so it did not matter; `color` is the tail of `background-color`, and without the lookbehind
    /// a query for it happily reads the wrong declaration's value. The trailing side was already
    /// safe — the `\s*:` means `--bs-primary` cannot match `--bs-primary-rgb:`.
    /// </summary>
    private static string Declared(string css, string selector, string property)
    {
        var found = false;

        foreach (var (selectors, body) in Rules(css))
        {
            if (!selectors.Contains(selector, StringComparer.Ordinal)) continue;
            found = true;

            var value = Regex.Match(
                body, @"(?<![-\w])" + Regex.Escape(property) + @"\s*:\s*(?<value>[^;]+);");
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

    /// <summary>
    /// The two quiet columns on the command palette's SELECTED row, against every primary the row
    /// can be drawn on.
    ///
    /// Three fills, not two: the paper skin declares a primary per brightness, and the slate skin
    /// takes Bootstrap's #0d6efd unmodified in both. The colour is read out of the stylesheet and
    /// composited, so this catches a reintroduced alpha as well as a changed colour — which is the
    /// failure it was written for. At .8 alpha, the value this rule carried for most of the app's
    /// life, it measured 3.42:1 on the slate primary and 3.60:1 on the paper dark one; nothing
    /// below full opacity clears the floor on any of the three, since #0d6efd only reaches 4.50:1
    /// under solid white.
    ///
    /// One query covers both columns: .sub and .kind share the declaration.
    /// </summary>
    [Fact]
    public void The_selected_palette_row_quiet_columns_clear_the_text_floor()
    {
        var css = Css();
        var ink = Parse(Declared(css, ".palette-item.sel .sub", "color"));

        var fills = new (string What, string Value)[]
        {
            ("the slate primary", "#0d6efd"),
            ("the paper light primary", Declared(css, PaperRule(false), "--bs-primary")),
            ("the paper dark primary", Declared(css, PaperRule(true), "--bs-primary")),
        };

        foreach (var (what, value) in fills)
        {
            var fill = Parse(value);
            var ratio = Contrast(Composite(ink, fill), fill);

            Assert.True(ratio >= Aa,
                $"the selected palette row's subtitle is {ratio:0.00}:1 on {what} ({value}), "
                + $"under the {Aa}:1 floor");
        }
    }

    /// <summary>
    /// The nav's brand mark, which has to survive on a near-white bar and a near-black one.
    ///
    /// There is no ratio to compute here, and that is the point of the test rather than a gap in it:
    /// the mark is drawn in currentColor, so it IS the body colour and cannot be under-contrasted
    /// against a surface without the body text being under-contrasted with it. What CAN go wrong is
    /// someone giving one of its shapes a literal colour — which is exactly how the previous version
    /// of this mark failed, greyscaling to #454545 and dropping to 1.7:1 on the dark bar. So the
    /// invariant asserted is the one that has teeth: the mark names no colour of its own.
    ///
    /// Read from the copied markup rather than from a render, because a hardcoded fill is a property
    /// of how the SVG is WRITTEN — a rendered DOM would show the same attribute either way.
    ///
    /// One drawing serves both places the mark appears, so this covers the phone's centre tab too:
    /// there the same paths sit on a filled circle that sets `color: #fff`, and a literal colour
    /// would break that instance as readily as the bar's.
    /// </summary>
    [Fact]
    public void The_pack_mark_names_no_colour_of_its_own()
    {
        var mark = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "markup", "components", "PackMark.razor.txt"));

        var paints = Regex.Matches(mark, @"(?:fill|stroke)=""(?<value>[^""]*)""");
        Assert.True(paints.Count >= 3, $"only {paints.Count} paint attributes found in PackMark");

        foreach (Match paint in paints)
        {
            var value = paint.Groups["value"].Value;
            Assert.True(value is "currentColor" or "none",
                $"the pack mark paints with '{value}'; only currentColor and none adapt to both "
                + "themes and to the filled tab, and a literal colour is how the last version of "
                + "this mark vanished into the dark bar");
        }
    }

    /// <summary>
    /// Both places that show the mark use the shared component rather than their own copy of the
    /// paths. Asserted because the drawing was duplicated once already and the copies drifted within
    /// a single change.
    /// </summary>
    [Fact]
    public void Both_homes_of_the_pack_mark_use_the_shared_drawing()
    {
        foreach (var (dir, file) in new[] { ("layout", "NavMenu"), ("components", "TabBar") })
        {
            var markup = File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "markup", dir, $"{file}.razor.txt"));

            Assert.Contains("<PackMark", markup);
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

        // The selected palette row's old .8-alpha white, on the fill it sat on. Named here rather
        // than parsed, because the point of it is to be a value the stylesheet no longer contains:
        // it is what the check above would have failed had it existed at the time.
        var dimmed = new Rgba(255, 255, 255, 0.8);
        var sel = Parse("#0d6efd");

        Assert.True(Contrast(Composite(dimmed, sel), sel) < Aa);
    }

    /// <summary>
    /// The paper skin's outline buttons, which are a full second set: Bootstrap's compiled variants
    /// are literal hex, so a skin that changed only the tokens would leave these the old blue, and
    /// each new colour has to clear the floor on the new surfaces rather than on the ones the slate
    /// values were measured against.
    /// </summary>
    [Theory]
    [InlineData(".btn-outline-primary", false)]
    [InlineData(".btn-outline-secondary", false)]
    [InlineData(".btn-outline-danger", false)]
    [InlineData(".btn-outline-primary", true)]
    [InlineData(".btn-outline-secondary", true)]
    [InlineData(".btn-outline-danger", true)]
    public void A_paper_outline_button_clears_the_contrast_floor(string variant, bool dark)
    {
        var css = Css();
        var colour = Parse(Declared(css, $"{PaperRule(dark)} {variant}", "--bs-btn-color"));
        var (panel, body) = PaperSurfaces(css, dark);

        foreach (var surface in new[] { panel, body })
        {
            var ratio = Contrast(colour, Parse(surface));
            Assert.True(ratio >= Aa,
                $"paper {variant} is {ratio:0.00}:1 on {surface} in {(dark ? "dark" : "light")}, "
                + $"under the {Aa}:1 floor");
        }
    }

    /// <summary>
    /// White on the paper skin's FILLED buttons. The skin restates the background of each variant
    /// and inherits Bootstrap's white label, so the pairing is one this stylesheet is responsible
    /// for even though it only wrote half of it.
    /// </summary>
    [Theory]
    [InlineData(".btn-primary", false)]
    [InlineData(".btn-secondary", false)]
    [InlineData(".btn-danger", false)]
    [InlineData(".btn-primary", true)]
    [InlineData(".btn-secondary", true)]
    [InlineData(".btn-danger", true)]
    public void A_paper_filled_button_clears_the_contrast_floor(string variant, bool dark)
    {
        var fill = Parse(Declared(Css(), $"{PaperRule(dark)} {variant}", "--bs-btn-bg"));
        var ratio = Contrast(Parse("#ffffff"), fill);

        Assert.True(ratio >= Aa,
            $"white on paper {variant} is {ratio:0.00}:1 in {(dark ? "dark" : "light")}, "
            + $"under the {Aa}:1 floor");
    }

    /// <summary>
    /// The paper skin's quietest copy, composited onto its own surfaces. Separate from the slate
    /// check because the skin sets the alpha AND the colour it is an alpha of.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Paper_de_emphasised_text_clears_the_contrast_floor(bool dark)
    {
        var css = Css();
        var colour = Parse(Declared(css, PaperRule(dark), "--bs-tertiary-color"));
        var (panel, body) = PaperSurfaces(css, dark);

        foreach (var surface in new[] { panel, body })
        {
            var bg = Parse(surface);
            var ratio = Contrast(Composite(colour, bg), bg);

            Assert.True(ratio >= Aa,
                $"paper --bs-tertiary-color is {ratio:0.00}:1 on {surface} in "
                + $"{(dark ? "dark" : "light")}, under the {Aa}:1 floor");
        }
    }

    /// <summary>
    /// The want heart in the paper skin, both states, on the paper caption. The idle heart's
    /// alpha comes from the skin's own --bs-secondary-color rather than from Bootstrap's, so it is
    /// parsed and multiplied by the .85 the element carries instead of being reconstructed by hand.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_paper_want_heart_clears_the_non_text_floor(bool dark)
    {
        const double NonText = 3.0;

        var css = Css();
        var rule = PaperRule(dark);
        var surface = Parse(PaperSurfaces(css, dark).Panel);

        var on = Parse(Declared(css, rule, "--pp-want-ink"));
        var onRatio = Contrast(on, surface);
        Assert.True(onRatio >= NonText,
            $"the filled heart is {onRatio:0.00}:1 on the paper caption in "
            + $"{(dark ? "dark" : "light")}, under the {NonText}:1 floor");

        var secondary = Parse(Declared(css, rule, "--bs-secondary-color"));
        var idle = secondary with { A = secondary.A * 0.85 };
        var idleRatio = Contrast(Composite(idle, surface), surface);

        Assert.True(idleRatio >= NonText,
            $"the empty heart is {idleRatio:0.00}:1 on the paper caption in "
            + $"{(dark ? "dark" : "light")}");
    }

    /// <summary>
    /// The count badge under the paper skin. The slate check above pins white on Bootstrap's
    /// unmodified primary; this skin supplies its own, in both brightnesses, and the badge is small
    /// text either way.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_paper_count_badge_clears_the_text_floor(bool dark)
    {
        var fill = Parse(Declared(Css(), PaperRule(dark), "--bs-primary"));
        var ratio = Contrast(Parse("#ffffff"), fill);

        Assert.True(ratio >= Aa,
            $"white on the paper primary is {ratio:0.00}:1 in {(dark ? "dark" : "light")}, "
            + $"under the {Aa}:1 floor — every fill in app.css that paints with --bs-primary "
            + "puts white on it");
    }

    /// <summary>
    /// Structural lines, which carry WCAG 1.4.11's 3:1 for a meaningful non-text boundary rather
    /// than the text floor. Asserted for the paper skin only: it is the one that moves the page
    /// background as well as the line, so the ratio is not the one that was measured for the slate
    /// skin, and in dark it is a deliberate departure from what Bootstrap ships.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_paper_area_border_clears_the_non_text_floor(bool dark)
    {
        const double NonText = 3.0;

        var css = Css();
        var line = Parse(Declared(css, PaperRule(dark), "--bs-border-color"));
        var body = Parse(PaperSurfaces(css, dark).Body);
        var ratio = Contrast(line, body);

        Assert.True(ratio >= NonText,
            $"the paper area border is {ratio:0.00}:1 on the body in "
            + $"{(dark ? "dark" : "light")}, under the {NonText}:1 floor for a boundary");
    }

    /// <summary>
    /// The accent as INK, for the six rules that use --bs-primary as a text or icon colour rather
    /// than as a fill.
    ///
    /// One value cannot do both jobs in the dark form: fill-safe under white text means 3.4:1 as
    /// text on the raised surface, which is under the floor. --pp-accent-ink is what those rules
    /// point at, and it is the half of the split that carries a contrast obligation.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_accent_as_ink_clears_the_contrast_floor(bool dark)
    {
        var css = Css();
        var ink = Parse(Declared(css, PaperRule(dark), "--pp-accent-ink"));
        var (panel, body) = PaperSurfaces(css, dark);

        foreach (var surface in new[] { panel, body })
        {
            var ratio = Contrast(ink, Parse(surface));
            Assert.True(ratio >= Aa,
                $"--pp-accent-ink is {ratio:0.00}:1 on {surface} in {(dark ? "dark" : "light")}, "
                + $"under the {Aa}:1 floor");
        }
    }

    /// <summary>
    /// A filled destructive button has to be tellable apart from a filled primary one.
    ///
    /// This skin has ONE accent, so both are red and the separation is luminance rather than hue —
    /// a deliberate trade, and the only one in the skin that a contrast ratio can actually police.
    /// 1.8:1 is the floor asserted here rather than the 3:1 of a boundary: the two fills never
    /// touch each other, so this is a legibility-of-difference claim, not a WCAG one. Both
    /// measured at 2.05:1 when they were chosen, which leaves room to adjust a hover without
    /// silently collapsing the pair.
    ///
    /// It is asserted BOTH ways round because "danger is the darker one" inverts between the two
    /// brightnesses, and an inversion that happened by accident would pass a one-sided check.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_two_reds_stay_tellable_apart(bool dark)
    {
        const double Separation = 1.8;

        var css = Css();
        var primary = Parse(Declared(css, $"{PaperRule(dark)} .btn-primary", "--bs-btn-bg"));
        var danger = Parse(Declared(css, $"{PaperRule(dark)} .btn-danger", "--bs-btn-bg"));

        var ratio = Contrast(primary, danger);
        Assert.True(ratio >= Separation,
            $"the filled primary and the filled destructive button are {ratio:0.00}:1 apart in "
            + $"{(dark ? "dark" : "light")}, under the {Separation}:1 this skin relies on to tell "
            + "two reds apart");

        // The destructive one is the deeper of the two in light and, because the token has to be
        // legible as ink on a near-black page, the deeper FILL in dark as well.
        Assert.True(Luminance(danger) < Luminance(primary),
            $"the destructive fill is no longer the deeper red in {(dark ? "dark" : "light")}");
    }

    private readonly record struct Rgba(double R, double G, double B, double A = 1);

    private static Rgba Parse(string value)
    {
        var v = value.Trim();

        if (v.StartsWith('#'))
        {
            // Three-digit shorthand expanded rather than rejected. Every value read here used to be
            // one this test's own authors had written long-hand; now that ordinary declarations are
            // parsed too, #fff is a form the stylesheet genuinely uses, and the six-digit-only
            // version threw an index error rather than saying what it could not read.
            var h = v[1..];
            if (h.Length is 3 or 4) h = string.Concat(h[..3].Select(c => new string(c, 2)));

            Assert.True(h.Length >= 6, $"cannot parse colour '{value}'");

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
