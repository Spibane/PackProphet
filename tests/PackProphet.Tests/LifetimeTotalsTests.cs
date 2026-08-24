namespace PackProphet.Tests;

using PackProphet.Engine;
using PackProphet.State;

public class LifetimeTotalsTests
{
    private static readonly DateTimeOffset Read = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private static PackOpenEvent Pack(DateTimeOffset at) => new(at, "A1", "Mewtwo", "Regular Pack", []);

    private static WonderOfferEvent Offer(DateTimeOffset at, bool taken) =>
        new(at, [], 1, taken, null);

    [Fact]
    public void Adds_packs_logged_after_the_counter_was_read()
    {
        var totals = new LifetimeTotals(1200, 40, Read);

        var log = new[] { Pack(Read.AddHours(1)), Pack(Read.AddDays(3)) };

        Assert.Equal(1202, totals.PacksWith(log));
    }

    [Fact]
    public void Does_not_count_packs_the_game_had_already_counted()
    {
        // The case this design exists for: someone logs a week here, THEN reads the game's
        // lifetime figure. Those packs are inside the game's number already, so counting the log
        // wholesale would report every one of them twice.
        var totals = new LifetimeTotals(1200, 40, Read);

        var log = new[]
        {
            Pack(Read.AddDays(-7)),
            Pack(Read.AddDays(-1)),
            Pack(Read.AddMinutes(30)),
        };

        Assert.Equal(1201, totals.PacksWith(log));
    }

    [Fact]
    public void Counts_only_wonder_offers_that_were_taken()
    {
        // Every offer SEEN is logged, because that is what makes the reservation threshold
        // learnable. The game's counter is of picks actually made, so skipped offers must not
        // inflate it.
        var totals = new LifetimeTotals(0, 40, Read);

        var log = new[]
        {
            Offer(Read.AddHours(1), taken: true),
            Offer(Read.AddHours(2), taken: false),
            Offer(Read.AddHours(3), taken: false),
            Offer(Read.AddHours(4), taken: true),
        };

        Assert.Equal(42, totals.WonderPicksWith(log));
    }

    [Fact]
    public void An_empty_log_leaves_the_baseline_alone()
    {
        var totals = new LifetimeTotals(1200, 40, Read);

        Assert.Equal(1200, totals.PacksWith([]));
        Assert.Equal(40, totals.WonderPicksWith([]));
    }

    [Fact]
    public void Survives_a_round_trip_through_the_saved_state()
    {
        // An optional property rather than a constructor parameter, so it has to be checked that
        // the serializer carries it: a silently dropped baseline would show up as History
        // forgetting a player's whole history on reload.
        var state = AppState.Fresh();
        var profile = state.Active with { Lifetime = new LifetimeTotals(1200, 40, Read) };
        state = state with { Profiles = [profile] };

        var json = StateSerializer.Serialize(state);
        var back = StateSerializer.Deserialize(json);

        Assert.NotNull(back);
        var lifetime = back!.Active.Lifetime;
        Assert.NotNull(lifetime);
        Assert.Equal(1200, lifetime!.PacksOpened);
        Assert.Equal(40, lifetime.WonderPicks);
        Assert.Equal(Read, lifetime.At);
    }

    [Fact]
    public void The_in_game_board_survives_a_round_trip_too()
    {
        // Same reason as the lifetime baseline: an optional property the serializer could silently
        // drop, and losing it would mean the next recommendation asked for twenty entries instead
        // of two swaps.
        var state = AppState.Fresh();
        var profile = state.Active with { TradeBoard = ["a.webp", "b.webp"] };
        state = state with { Profiles = [profile] };

        var back = StateSerializer.Deserialize(StateSerializer.Serialize(state));

        Assert.Equal(["a.webp", "b.webp"], back!.Active.TradeBoard);
    }

    [Fact]
    public void Board_settings_persist_because_forgetting_them_would_invent_swaps()
    {
        // Not a convenience. The board advisor reports changes against what is already listed, so a
        // setting that reset on navigation would have the page demand swaps caused by nothing but
        // its own forgetfulness.
        var fresh = AppState.Fresh();
        Assert.Equal(TradeBoardAdvisor.DefaultLiquidSlots, fresh.Prefs.BoardLiquidSlots);
        Assert.Equal("with", fresh.Prefs.BoardFoils);
        Assert.Equal(0, fresh.Prefs.BoardMinimumCost);

        var state = fresh with
        {
            Prefs = fresh.Prefs with
            {
                BoardLiquidSlots = 0,
                BoardMinimumCost = 50,
                BoardFoils = "without"
            }
        };

        var back = StateSerializer.Deserialize(StateSerializer.Serialize(state))!;

        // Zero especially: it is a deliberate choice that a "missing means default" reader would
        // silently turn back into four.
        Assert.Equal(0, back.Prefs.BoardLiquidSlots);
        Assert.Equal(50, back.Prefs.BoardMinimumCost);
        Assert.Equal("without", back.Prefs.BoardFoils);
    }
}
