namespace PackProphet.App.Tests;

using System.Text.RegularExpressions;
using PackProphet.Services;

/// <summary>
/// Boolean ARIA states have to be written as words.
///
/// Blazor omits an attribute whose value is <c>false</c> — correct for HTML's own boolean
/// attributes, since <c>disabled="false"</c> would disable the control, and wrong for every ARIA
/// state, where the two values are the strings "true" and "false" and the ABSENCE of the attribute
/// is a third, different thing: it says the element has no such state.
///
/// So <c>aria-pressed="@Pressed"</c> announced an unpressed toggle as an ordinary button. The one
/// state that needed saying was the only one that said nothing, and nothing in the markup looked
/// wrong — the attribute was written, it just never arrived.
///
/// This reads the .razor sources rather than the rendered DOM on purpose. A dropped attribute is
/// invisible from the other side: rendering shows an element with no aria-pressed, which is exactly
/// what a plain button looks like, so there is nothing there to assert against.
/// </summary>
public class AriaStateTests
{
    /// <summary>
    /// The states whose values are the words "true" and "false". Deliberately not aria-hidden,
    /// aria-current or aria-live: aria-hidden IS meaningful by its absence, aria-current takes a
    /// token, and aria-live takes "polite" or "assertive".
    /// </summary>
    private static readonly string[] BooleanStates =
        ["aria-pressed", "aria-expanded", "aria-checked", "aria-selected", "aria-disabled",
         "aria-busy", "aria-invalid", "aria-modal", "aria-multiselectable", "aria-readonly",
         "aria-required"];

    private static IEnumerable<(string File, string Text)> Markup()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "markup");

        foreach (var file in Directory.GetFiles(root, "*.razor.txt", SearchOption.AllDirectories))
            yield return (Path.GetFileName(file), File.ReadAllText(file));
    }

    [Fact]
    public void TheMarkupIsThere_soAFailureHereMeansSomething()
    {
        // A scan that found no files would pass every assertion below by scanning nothing.
        var files = Markup().ToArray();

        Assert.True(files.Length > 20, $"only {files.Length} razor files were copied for scanning");
        Assert.Contains(files, f => f.File == "CardGrid.razor.txt");
        Assert.Contains(files, f => f.File == "Collection.razor.txt");
        Assert.Contains(files, f => f.File == "NavMenu.razor.txt");
    }

    [Fact]
    public void EveryBooleanAriaState_IsWrittenAsAWord()
    {
        // Either a literal, or through Aria.Flag. Anything else is a C# bool on its way to being
        // silently dropped in one of its two states.
        var offenders = new List<string>();

        foreach (var (file, text) in Markup())
        foreach (var state in BooleanStates)
        {
            foreach (var match in Regex.Matches(text, $@"{state}\s*=\s*""([^""]*)""").Cast<Match>())
            {
                var value = match.Groups[1].Value.Trim();

                if (value is "true" or "false") continue;
                if (value.Contains("Aria.Flag", StringComparison.Ordinal)) continue;

                var line = text[..match.Index].Count(c => c == '\n') + 1;
                offenders.Add($"{file}:{line}  {state}=\"{value}\"");
            }
        }

        Assert.True(offenders.Count == 0,
            "Boolean ARIA states must render the word, via Aria.Flag(...), or Blazor drops the "
            + "attribute in the false case and the control stops announcing itself as a toggle:\n  "
            + string.Join("\n  ", offenders));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void TheFlagIsTheWord_inBothDirections(bool on, string expected) =>
        Assert.Equal(expected, Aria.Flag(on));
}
