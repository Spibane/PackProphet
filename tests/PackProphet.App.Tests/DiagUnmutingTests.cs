namespace PackProphet.App.Tests;

using System.Text.RegularExpressions;

/// <summary>
/// That diag.js still catches an exception from every asynchronous entry point the app's own
/// JavaScript uses.
///
/// This is the whole of the iOS "Script error" investigation in KNOWN-ISSUES.md. Safari hands
/// window.onerror no message, no file, no line and no Error object when the script that threw is
/// cross-origin, so the report the phone can produce says only that something went wrong. A
/// try/catch around the callback is not subject to that: it sees the real Error, with its stack,
/// whatever origin the code came from.
///
/// So the net has to stay complete. A new kind of callback handed to the browser — a
/// requestIdleCallback, a PerformanceObserver — would escape the wrappers silently, and the only
/// symptom would be an opaque report months later that nobody can act on. Both directions are
/// asserted: everything the app uses is guarded, and the app uses nothing that is not on the list.
/// </summary>
public class DiagUnmutingTests
{
    private static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "js", name));

    private static readonly string Diag = Read("diag.js");

    /// <summary>
    /// diag.js with its commentary removed. It quotes the shapes it replaced -- the silent
    /// `navigator.clipboard?.` among them -- so a test that greps the raw text is asserting on the
    /// prose rather than on the code.
    /// </summary>
    private static readonly string DiagCode = Code(Read("diag.js"));

    /// <summary>
    /// Every way this app's JavaScript hands a callback to the browser to be run later, and so
    /// every place an exception can surface with no caller to catch it.
    /// </summary>
    private static readonly string[] EntryPoints =
    [
        "setTimeout", "setInterval", "requestAnimationFrame", "requestIdleCallback",
        "IntersectionObserver", "MutationObserver", "ResizeObserver", "PerformanceObserver",
        "addEventListener",
    ];

    /// <summary>
    /// Comments stripped before anything is counted as a use. Half these names appear in the
    /// commentary explaining why the code is shaped as it is — imgloader's header alone names
    /// IntersectionObserver twice — and a file that only discusses one is not a file that needs
    /// it guarded.
    /// </summary>
    private static string Code(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return string.Join("\n", withoutBlocks
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The app's own scripts, which is every file under wwwroot/js except diag.js itself — it is
    /// the thing under test, not a user of it — and the vendored QR decoder, which is third-party
    /// code this project does not write and would not change.
    /// </summary>
    private static IEnumerable<(string Name, string Code)> AppScripts() =>
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "js"), "*.js")
            .Select(Path.GetFileName)
            .Where(name => name is not ("diag.js" or "vendor-jsQR.js"))
            .Select(name => (Name: name!, Code: Code(Read(name!))));

    /// <summary>
    /// Whether diag.js takes the global over. Either it assigns a replacement — on window, or on
    /// EventTarget.prototype for listeners — or the name is in the list of observers it patches in
    /// a loop. Asserted against the mechanism rather than against a copy of the list, so the two
    /// cannot drift apart.
    /// </summary>
    private static bool Guarded(string entryPoint) =>
        Regex.IsMatch(Diag, $@"(window|EventTarget\.prototype)\.{entryPoint}\s*=")
        || Regex.IsMatch(Diag, $@"for \(const name of \[[^\]]*'{entryPoint}'");

    [Fact]
    public void Every_entry_point_the_app_uses_is_guarded()
    {
        var unguarded = EntryPoints
            .Where(e => !Guarded(e))
            .Select(e => (Entry: e, Users: AppScripts()
                .Where(s => Regex.IsMatch(s.Code, $@"\b{e}\b"))
                .Select(s => s.Name)
                .ToArray()))
            .Where(x => x.Users.Length > 0)
            .Select(x => $"{x.Entry} (used by {string.Join(", ", x.Users)})")
            .ToArray();

        Assert.True(unguarded.Length == 0,
            "diag.js does not wrap these, so an exception from one reaches Safari's muted "
            + "window.onerror and loses its message, file and line: " + string.Join("; ", unguarded));
    }

    [Fact]
    public void The_app_uses_no_entry_point_that_is_not_on_the_list()
    {
        // Anything shaped like a callback registration that this file has never heard of. The set
        // is deliberately small and named: the point is to fail when a NEW kind appears, so that
        // whoever adds it decides whether it needs guarding, rather than finding out from a
        // report with nothing in it.
        var suspects = new[]
        {
            "requestIdleCallback", "PerformanceObserver", "ReportingObserver",
            "queueMicrotask", "setImmediate", "onmessage", "BroadcastChannel", "new Worker",
        };

        var unknown = suspects
            .Where(s => !Guarded(s.Replace("new ", "")))
            .Select(s => (Suspect: s, Users: AppScripts()
                .Where(x => x.Code.Contains(s, StringComparison.Ordinal))
                .Select(x => x.Name)
                .ToArray()))
            .Where(x => x.Users.Length > 0)
            .Select(x => $"{x.Suspect} in {string.Join(", ", x.Users)}")
            .ToArray();

        Assert.True(unknown.Length == 0,
            "a new asynchronous entry point that diag.js does not wrap: " + string.Join("; ", unknown)
            + ". Either guard it in diag.js or add it to this test's list with a reason.");
    }

    [Fact]
    public void A_caught_exception_is_reported_with_its_stack_and_its_entry_point()
    {
        // The three things that make a caught report worth more than the muted one it replaces.
        Assert.Contains("err.stack", Diag);
        Assert.Contains("thrown in ", Diag);
        Assert.Matches(@"caught\+\+", Diag);
    }

    [Fact]
    public void The_muted_report_says_whether_any_of_the_apps_callbacks_had_thrown()
    {
        // The decisive line. Zero caught, alongside a muted error, means the throw came from no
        // callback this app registered — which is the fact the muted event cannot state itself,
        // and the reason the report is worth reading at all.
        Assert.Contains("callbacks that have thrown", Diag);
    }

    [Fact]
    public void Resource_failures_are_listened_for_in_the_capture_phase()
    {
        // An error event for a failed subresource fires at the element and does not bubble, so a
        // plain window listener never receives one. The file carried an `e.target.tagName === 'IMG'`
        // guard that read as though it did, and which had therefore never fired: every blocked or
        // failed script, stylesheet and icon on the page was invisible. Capture is the only phase
        // that reaches them.
        Assert.Matches(@"nativeAdd\.call\(window, 'error'[\s\S]{0,1600}\}, true\)", DiagCode);
    }

    [Fact]
    public void Copying_the_report_does_not_depend_on_a_secure_context()
    {
        // navigator.clipboard is undefined outside a secure context, which is every phone reading
        // this box over plain HTTP to a LAN address -- the case it exists for. Written as
        // `navigator.clipboard?.writeText(...)` it silently did nothing there.
        Assert.DoesNotContain("navigator.clipboard?.", DiagCode);
        Assert.Contains("execCommand('copy')", DiagCode);
        Assert.Contains("setSelectionRange", DiagCode);
    }

    [Fact]
    public void The_muted_report_says_where_the_parser_had_got_to()
    {
        // The error arrives at +0.0s, before the runtime and before anything cross-origin has
        // loaded, so "which script was in play" is the whole question and readyState plus the
        // count of script elements answers it.
        Assert.Contains("readyState=", Diag);
        Assert.Contains("script elements reached", Diag);
        // And whether the import map is the sort of thing that could have thrown: a map the
        // browser rejects is reported with no script behind it, which is this exact shape.
        Assert.Contains("Import map: ", Diag);
    }

    [Fact]
    public void A_muted_error_that_is_provably_not_ours_is_counted_rather_than_shown()
    {
        // The signature of a script the BROWSER injected: the throw was cross-origin (so it is
        // muted), no wrapper caught anything (so it was no callback of ours), and there is no
        // cross-origin subresource on the page (so there is no foreign code here to have thrown
        // it). Every third-party iOS browser does this and Safari does not -- see KNOWN-ISSUES.md.
        // Opening a red panel on a visitor's phone for another program's bug is noise.
        Assert.Matches(@"if \(caught === 0 && foreignSummary\.length === 0\)", DiagCode);
        Assert.Contains("mutedSuppressed++", DiagCode);

        // Counted, not discarded: if anything real fires afterwards it is shown alongside it, so
        // suppressing this can never hide the context of a genuine error.
        Assert.Contains("from the browser rather than from this page", DiagCode);
        Assert.Contains("window.diagMuted", DiagCode);
    }

    [Fact]
    public void The_report_names_the_browser_and_whether_it_injects_scripts()
    {
        // Every browser on iOS is WebKit, so "only on iOS" reads as a WebKit problem when it is
        // really the wrapper app. This line is what turns the error from unexplained into
        // not-ours, so it is the first thing a report should be read for.
        foreach (var token in new[] { "CriOS", "FxiOS", "EdgiOS", "Brave", "DuckDuckGo" })
            Assert.Contains(token, DiagCode);
        Assert.Contains("Browser: ", DiagCode);
    }

    [Fact]
    public void Listeners_can_still_be_removed_after_being_wrapped()
    {
        // Every dispose() in the app removes a listener by the function it added, and after
        // wrapping that is no longer the function the browser holds. Without the translation
        // below, removeEventListener silently stops working and the leak is invisible.
        Assert.Contains("EventTarget.prototype.removeEventListener = ", Diag);
        Assert.Contains("listenerWrappers", Diag);
    }
}
