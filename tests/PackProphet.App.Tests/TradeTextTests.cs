namespace PackProphet.App.Tests;

using Microsoft.AspNetCore.Components;
using PackProphet.Pages;

/// <summary>
/// A chase list as a few lines of text.
///
/// The share link is the better artefact and the wrong shape for where these trades happen: a
/// Discord thread takes text, nobody opens a stranger's link, and a reader with the game open on
/// the same phone cannot follow one and come back.
/// </summary>
public class TradeTextTests : AppHost
{
    private async Task<IRenderedComponent<ChaseLists>> ListPageAsync(string listId)
    {
        await ReadyAsync();
        return RenderComponent<ChaseLists>(p => p.Add(w => w.Id, listId));
    }

    /// <summary>A list wanting two of one card and one of another, neither owned.</summary>
    private (string Id, string FirstName) Seed()
    {
        var id = Session.CreateChaseList("Chase");
        var cards = Session.Index.All.Where(c => c.Set == "A1").Take(2).ToArray();

        Session.SetWanted(id, cards[0].OwnershipKey, 2);
        Session.SetWanted(id, cards[1].OwnershipKey, 1);

        return (id, cards[0].Name);
    }

    private static string TextOf(IRenderedComponent<ChaseLists> page)
    {
        page.FindAll("button").First(b => b.TextContent.Contains("Copy as Text")).Click();
        return page.Find(".trade-text").TextContent;
    }

    [Fact]
    public async Task The_text_names_the_list_and_every_card_still_short()
    {
        await ReadyAsync();
        var (id, firstName) = Seed();
        var page = await ListPageAsync(id);

        var text = TextOf(page);

        Assert.Contains("Chase", text);
        Assert.Contains(firstName, text);

        // Two wanted and none owned is a shortfall of two, and the count only appears where it is
        // more than one — "x1" on every line is noise.
        Assert.Contains("x2", text);

        // The set and number lead the line, because two cards share a name often enough that a
        // name alone is ambiguous.
        Assert.Matches(@"A1-\d+ ", text);
    }

    [Fact]
    public async Task Cards_you_already_own_are_left_out()
    {
        // A chase list keeps its rows once you own them, and asking a stranger for a card you have
        // wastes their time and your credibility.
        await ReadyAsync();
        var (id, firstName) = Seed();

        var owned = Session.Index.All.First(c => c.Name == firstName);
        Session.Adjust([owned.OwnershipKey], 2);

        var page = await ListPageAsync(id);
        var text = TextOf(page);

        Assert.DoesNotContain(firstName, text);
    }

    [Fact]
    public async Task A_finished_list_says_so_rather_than_producing_an_empty_message()
    {
        await ReadyAsync();
        var id = Session.CreateChaseList("Done");
        var page = await ListPageAsync(id);

        var text = TextOf(page);

        Assert.Contains("nothing outstanding", text);
    }

    [Fact]
    public async Task The_in_game_name_is_in_the_message_when_there_is_one()
    {
        // A want-list nobody can act on is half a share: the reader learns exactly what to send and
        // has no way to find you.
        await ReadyAsync();
        var (id, _) = Seed();

        var page = await ListPageAsync(id);
        Assert.DoesNotContain("Find me in-game", TextOf(page));

        Session.SetInGameName(Session.Profile.Id, "ASH-1234");

        var withName = await ListPageAsync(id);
        Assert.Contains("ASH-1234", TextOf(withName));
    }

    [Fact]
    public async Task Every_line_carries_the_rarity_because_the_trade_rules_turn_on_it()
    {
        await ReadyAsync();
        var (id, _) = Seed();
        var page = await ListPageAsync(id);

        var lines = TextOf(page)
            .Split('\n')
            .Where(l => l.StartsWith("A1-", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(lines);
        Assert.All(lines, l => Assert.Contains("(", l));
    }
}
