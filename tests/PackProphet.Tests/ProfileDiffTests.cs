namespace PackProphet.Tests;

using PackProphet.Data;
using PackProphet.Domain;
using PackProphet.Engine;
using PackProphet.State;

public class ProfileDiffTests
{
    private static CardIndex Ix => Snapshot.Index();

    private static TradeQueue Queue => new(Ix, Snapshot.Odds(), Snapshot.Rarities());

    private static ProfileDiff Diff => new(Ix, Snapshot.Odds(), Snapshot.Rarities(), Queue);

    private static Func<string, RarityPlan> Everything =>
        _ => RarityPlan.Uniform(Ix.Ladder.Everything);

    private static Resources Rich => Resources.Empty with
    {
        Shinedust = 1_000_000,
        Trade = new ResourcePool(GameRules.StaminaCap, 0)
    };

    /// <summary>Distinct tradeable cards at one rarity, which is what a swap is constrained by.</summary>
    private static PocketCard[] AtRarity(string code, int count) =>
        Ix.All.Where(c => c.Rarity == code && !c.IsPromo)
            .DistinctBy(c => c.OwnershipKey)
            .Take(count)
            .ToArray();

    private static DiffSide Side(
        string name,
        IEnumerable<(PocketCard Card, int Copies)> holds,
        IEnumerable<PocketCard> wants,
        Resources? resources = null)
    {
        var owned = new Collection(holds.ToDictionary(h => h.Card.OwnershipKey, h => h.Copies));
        var demands = wants.Select(c => new Demand(c.OwnershipKey, 1, new[] { c })).ToArray();

        return new DiffSide(name, name, owned, Everything, demands, resources ?? Rich);
    }

    [Fact]
    public void Finds_the_swap_where_both_sides_get_something_they_wanted()
    {
        // The case the whole feature exists for, and the only one that costs nothing but the dust:
        // each account holds a spare the other is missing, at the same rarity.
        var (a, b) = (AtRarity("SR", 2)[0], AtRarity("SR", 2)[1]);

        var mine = Side("main", [(a, 2)], [b]);
        var theirs = Side("alt", [(b, 2)], [a]);

        var swap = Assert.Single(Diff.Compare(mine, theirs).Swaps);

        Assert.True(swap.Mutual);
        Assert.Equal(b.OwnershipKey, swap.Incoming.OwnershipKey);
        Assert.Equal(a.OwnershipKey, swap.Outgoing.OwnershipKey);
        Assert.Equal(Snapshot.Rarities()["SR"].TradePrice, swap.Dust);
    }

    [Fact]
    public void Pays_with_filler_when_nothing_at_that_rarity_is_wanted_back()
    {
        // Still a trade worth making, but ONE want closed rather than two - so it must not be
        // reported as a mutual swap. At 1-star, because a Share would carry a diamond for free
        // and there would be nothing to pay for.
        var cards = AtRarity("AR", 3);
        var want = cards[0];
        var filler = cards[1];

        var mine = Side("main", [(filler, 2)], [want]);
        var theirs = Side("alt", [(want, 2)], []);

        var result = Diff.Compare(mine, theirs);
        var swap = Assert.Single(result.Swaps);

        Assert.False(swap.Mutual);
        Assert.Equal(want.OwnershipKey, swap.Incoming.OwnershipKey);
        Assert.Equal(filler.OwnershipKey, swap.Outgoing.OwnershipKey);
        Assert.Equal(0, result.Mutual);
    }

    [Fact]
    public void Reports_a_want_it_cannot_pay_for_rather_than_dropping_it()
    {
        // A main with no spare 2-star cannot take the alt's spare 2-star, however much it wants it.
        // Omitting the row would read as the card not being there at all.
        var want = AtRarity("SR", 1)[0];

        var mine = Side("main", [], [want]);
        var theirs = Side("alt", [(want, 2)], []);

        var result = Diff.Compare(mine, theirs);

        Assert.Empty(result.Swaps);
        Assert.Equal(want.OwnershipKey, Assert.Single(result.Unpayable).Card.OwnershipKey);
    }

    [Fact]
    public void Never_pays_across_rarities()
    {
        // Trades are same-rarity only, so a shelf full of spare rares does nothing for a 2-star.
        var want = AtRarity("SR", 1)[0];
        var spare = AtRarity("R", 1)[0];

        var mine = Side("main", [(spare, 3)], [want]);
        var theirs = Side("alt", [(want, 2)], []);

        var result = Diff.Compare(mine, theirs);

        Assert.Empty(result.Swaps);
        Assert.Single(result.Unpayable);
    }

    [Fact]
    public void Leaves_out_the_rarities_that_cannot_be_traded_at_all()
    {
        foreach (var code in new[] { "IM", "UR" })
        {
            var cards = Ix.All.Where(c => c.Rarity == code).DistinctBy(c => c.OwnershipKey)
                          .Take(2).ToArray();

            var mine = Side("main", [(cards[0], 2)], [cards[1]]);
            var theirs = Side("alt", [(cards[1], 2)], [cards[0]]);

            var result = Diff.Compare(mine, theirs);

            // Not a swap, and not an unpayable want either: there is no advice to give, because
            // no spare and no balance ever makes this trade legal.
            Assert.True(result.Nothing, $"{code} should offer nothing at all");
        }
    }

    [Fact]
    public void Counts_copies_rather_than_cards()
    {
        // Four copies of a card the other side needs once is one trade, not three.
        var cards = AtRarity("AR", 3);
        var want = cards[0];

        var mine = Side("main", [(cards[1], 4)], [want]);
        var theirs = Side("alt", [(want, 4)], []);

        var result = Diff.Compare(mine, theirs);

        Assert.Single(result.Swaps);
        // The spares that were not needed stay visible: they are what makes the next swap possible.
        Assert.Empty(result.Unpayable);
    }

    [Fact]
    public void Stops_promising_trades_once_either_side_runs_out_of_stamina()
    {
        // Stamina is the binding constraint on trading, and BOTH accounts spend it. An alt with one
        // stamina caps the plan at one trade today however many swaps exist.
        var cards = AtRarity("AR", 6);

        var mine = Side("main", [(cards[0], 2), (cards[1], 2), (cards[2], 2)],
                        [cards[3], cards[4], cards[5]]);

        var theirs = Side("alt", [(cards[3], 2), (cards[4], 2), (cards[5], 2)],
                          [cards[0], cards[1], cards[2]],
                          Rich with { Trade = new ResourcePool(1, 0) });

        var result = Diff.Compare(mine, theirs);

        Assert.Equal(3, result.Swaps.Count);
        Assert.Equal(1, result.ReadyNow);
        Assert.Equal(TradeGate.NoStamina,
                     result.Swaps.First(s => !s.Ready).TheirsBlocking);
    }

    [Fact]
    public void Dust_is_checked_on_both_sides_separately()
    {
        // The alt is the one short of dust, and saying so is the whole value of the row: the fix
        // is on the other account, and a single combined verdict could not point there.
        var (a, b) = (AtRarity("SR", 2)[0], AtRarity("SR", 2)[1]);
        var price = Snapshot.Rarities()["SR"].TradePrice!.Value;

        var mine = Side("main", [(a, 2)], [b]);
        var theirs = Side("alt", [(b, 2)], [a], Rich with { Shinedust = price - 1 });

        var swap = Assert.Single(Diff.Compare(mine, theirs).Swaps);

        Assert.Equal(TradeGate.None, swap.MineBlocking);
        Assert.Equal(TradeGate.NotEnoughDust, swap.TheirsBlocking);
        Assert.False(swap.Ready);
    }

    [Fact]
    public void A_profile_compared_with_itself_offers_nothing()
    {
        // Not guarded against, because it answers itself: a card cannot be both spare and
        // outstanding on the same side.
        var cards = AtRarity("R", 2);
        var side = Side("main", [(cards[0], 2)], [cards[1]]);

        Assert.Empty(Diff.Compare(side, side).Swaps);
    }

    [Fact]
    public void Ranks_the_swap_that_saves_the_most_first()
    {
        // Value is expected packs saved across BOTH accounts, so a mutual swap outranks a
        // one-sided one of the same card - it closes two wants for the same stamina.
        var cards = AtRarity("AR", 4);

        var mine = Side("main", [(cards[0], 2), (cards[1], 2)], [cards[2], cards[3]]);
        var theirs = Side("alt", [(cards[2], 2), (cards[3], 2)], [cards[0]]);

        var result = Diff.Compare(mine, theirs);

        Assert.Equal(2, result.Swaps.Count);
        Assert.True(result.Swaps[0].Mutual);
        Assert.False(result.Swaps[1].Mutual);
    }

    // ---- Shares -------------------------------------------------------------------------
    // A Share is one-way: a friend sends a 1-4 diamond card and gets nothing back, one a day. So
    // between two of your own accounts it is strictly better than a trade wherever it applies,
    // and the diamonds must leave the swap machinery entirely rather than being offered both ways.

    [Fact]
    public void Sends_the_diamonds_as_shares_instead_of_trading_them()
    {
        var (a, b) = (AtRarity("R", 2)[0], AtRarity("R", 2)[1]);

        var mine = Side("main", [(a, 2)], [b]);
        var theirs = Side("alt", [(b, 2)], [a]);

        var result = Diff.Compare(mine, theirs);

        // No trade at all: paying 1,200 dust and two stamina to move cards that can be handed
        // over for nothing is a worse deal in every respect.
        Assert.Empty(result.Swaps);
        Assert.Empty(result.Unpayable);

        Assert.Equal(b.OwnershipKey, Assert.Single(result.IncomingShares).Card.OwnershipKey);
        Assert.Equal(a.OwnershipKey, Assert.Single(result.OutgoingShares).Card.OwnershipKey);
    }

    [Fact]
    public void A_diamond_needs_no_card_back_and_no_spare_at_its_rarity()
    {
        // The case that is impossible as a trade and free as a Share. This is the whole reason
        // Shares had to be modelled rather than treated as a variety of trade.
        var want = AtRarity("RR", 1)[0];

        var mine = Side("main", [], [want]);
        var theirs = Side("alt", [(want, 2)], []);

        var result = Diff.Compare(mine, theirs);

        Assert.Empty(result.Unpayable);
        Assert.Equal(want.OwnershipKey, Assert.Single(result.IncomingShares).Card.OwnershipKey);
    }

    [Fact]
    public void Shares_cost_days_rather_than_dust_or_stamina()
    {
        // An account with no dust and no stamina can still receive every one of them, because a
        // Share spends neither. What it cannot do is receive them faster than one a day.
        var cards = AtRarity("R", 3);

        var mine = Side("main", [], cards, Resources.Empty);
        var theirs = Side("alt", cards.Select(c => (c, 2)), [], Resources.Empty);

        var result = Diff.Compare(mine, theirs);

        Assert.Equal(3, result.IncomingShares.Count);
        Assert.Equal(3, result.ShareDays);

        // Both directions run in parallel: each account has its own daily allowance, so the pace
        // is the busier direction and not the sum of the two.
        Assert.Equal(3, result.AllSharesDays);
    }

    [Fact]
    public void Stars_are_never_shareable_however_ordinary_they_are_to_trade()
    {
        // Shares stop at 4 diamonds, so everything above stays a trade problem - and that is the
        // line the two features are divided on.
        foreach (var code in new[] { "AR", "SR", "S" })
        {
            var want = AtRarity(code, 1)[0];

            var mine = Side("main", [], [want]);
            var theirs = Side("alt", [(want, 2)], []);

            var result = Diff.Compare(mine, theirs);

            Assert.Empty(result.IncomingShares);
            Assert.Single(result.Unpayable);
        }
    }

    [Fact]
    public void A_shared_card_is_not_also_offered_as_trade_payment()
    {
        // It can only be sent once. A spare the other side wants belongs in the share list, and
        // reusing it as filler for a star trade would hand the same card over twice.
        var diamond = AtRarity("R", 1)[0];
        var star = AtRarity("AR", 2);

        var mine = Side("main", [(diamond, 2)], [star[0]]);
        var theirs = Side("alt", [(star[0], 2)], [diamond]);

        var result = Diff.Compare(mine, theirs);

        Assert.Equal(diamond.OwnershipKey, Assert.Single(result.OutgoingShares).Card.OwnershipKey);

        // And the star want is still unpayable: a diamond cannot pay for a 1-star either way.
        Assert.Empty(result.Swaps);
        Assert.Single(result.Unpayable);
    }
}
