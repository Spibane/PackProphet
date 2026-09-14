namespace PackProphet.App.Tests;

using AngleSharp.Dom;

using PackProphet.Deck;
using PackProphet.Pages;

/// <summary>
/// The deck builder's Save button, and the one edit that could not reach it.
///
/// A deck has four editable fields and the page has a control for each: the name box, the card
/// rows, the energy buttons and the ★ that picks which card fronts the deck in the grid. Save is
/// disabled until the draft differs from what is stored, and the comparison that decides that
/// named three of the four. So starring a row edited the draft, left Save disabled, and the choice
/// was lost on the next reload — a control that appears to work and silently does nothing, which
/// is worse than one that is missing.
///
/// Tested through the page rather than against the comparison, because the bug was not in either
/// piece: both were right about what they did, and what was wrong was that one of them did not
/// know about a field the other could change. The next field added to a deck can go the same way,
/// and this is where that shows up.
/// </summary>
public class DeckFaceTests : AppHost
{
    private const string Id = "d1";

    /// <summary>
    /// A saved deck with real cards in it, since the ★ appears per card row. Two, so that
    /// "star the other one" is a state this can reach.
    /// </summary>
    private int[] Seed()
    {
        var nrs = Session.Index.All
            .Select(c => DeckBuilderNr.FromImage(c.Image))
            .Where(n => n is > 0)
            .Select(n => n!.Value)
            .Distinct()
            .Take(2)
            .ToArray();

        Session.Mutate(p => p with
        {
            Decks = [new SavedDeck(Id, "Zapdos beatdown", [.. nrs], [EnergyType.Lightning])],
        });

        return nrs;
    }

    private async Task<IRenderedComponent<DeckDetail>> PageAsync()
    {
        await ReadyAsync();
        Seed();
        return RenderComponent<DeckDetail>(p => p.Add(c => c.Id, Id));
    }

    private static IElement Save(IRenderedComponent<DeckDetail> page) =>
        page.FindAll("button").First(b => b.TextContent.Trim() == "Save");

    /// <summary>
    /// The ★ on one row, found fresh every time. bUnit's collection is refreshable and the
    /// element behind it is not: clicking replaces the row, so an element held across a click is
    /// a reference to markup that no longer exists.
    /// </summary>
    private static IElement Star(IRenderedComponent<DeckDetail> page, int row) =>
        page.FindAll(".face-pick").ElementAt(row);

    [Fact]
    public async Task A_saved_deck_opens_with_nothing_to_save()
    {
        // The baseline the rest of this rests on: if Save were enabled on arrival, a test that
        // starred a card and found it enabled would prove nothing.
        var page = await PageAsync();

        Assert.True(Save(page).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Starring_a_card_is_something_to_save()
    {
        var page = await PageAsync();

        Star(page, 0).Click();

        Assert.False(Save(page).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Unstarring_it_again_is_too()
    {
        // The other direction, which a comparison written as "the draft has a face" rather than
        // "the draft's face differs" would get wrong: clearing a stored face is also an edit.
        var page = await PageAsync();

        Star(page, 0).Click();
        Save(page).Click();
        Assert.True(Save(page).HasAttribute("disabled"));

        Star(page, 0).Click();

        Assert.False(Save(page).HasAttribute("disabled"));
    }

    [Fact]
    public async Task And_the_star_survives_the_save()
    {
        var page = await PageAsync();
        var nr = Session.DeckById(Id)!.DeckBuilderNrs[1];

        Star(page, 1).Click();
        Save(page).Click();

        Assert.Equal(nr, Session.DeckById(Id)!.FaceNr);
    }

    /// <summary>
    /// Moving the star from one card to another, which is the edit most likely to be mistaken for
    /// no edit at all: the draft has a face before and after, and only the number changes.
    /// </summary>
    [Fact]
    public async Task Moving_the_star_to_another_card_is_an_edit()
    {
        var page = await PageAsync();

        Star(page, 0).Click();
        Save(page).Click();

        Star(page, 1).Click();

        Assert.False(Save(page).HasAttribute("disabled"));
    }
}
