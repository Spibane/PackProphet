namespace PackProphet.Tests;

using PackProphet.Domain;
using PackProphet.Engine;
using PackProphet.State;

public class ResourcePlanTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private static ResourcePool Pool(int balance, int hourglasses = 0, DateTimeOffset? asOf = null) =>
        new(balance, hourglasses) { AsOf = asOf };

    [Fact]
    public void Regenerates_one_unit_every_twelve_hours()
    {
        var outlook = ResourcePlan.Project(Pool(1, asOf: Noon), Noon.AddHours(25));

        // 25 hours is two whole intervals, not two and a bit rounded up.
        Assert.Equal(3, outlook.Balance);
        Assert.Equal(1, outlook.Entered);
    }

    [Fact]
    public void Stops_at_the_cap_and_says_what_the_wait_cost()
    {
        // Entered at 3, so it fills 24 hours later. Four days on, that is three days spent full.
        var outlook = ResourcePlan.Project(Pool(3, asOf: Noon), Noon.AddDays(4));

        Assert.Equal(GameRules.StaminaCap, outlook.Balance);
        Assert.True(outlook.AtCap);
        Assert.Equal(6, outlook.WastedSinceFull);   // 72 hours full, one unit per 12
        Assert.Null(outlook.UntilNext);
        Assert.Null(outlook.UntilFull);
    }

    [Fact]
    public void Does_not_blame_the_cap_for_time_spent_filling()
    {
        // An empty pool entered four days ago has been full for 12 hours, not four days. Counting
        // the fill time as waste would tell someone they had lost stamina they never had.
        var outlook = ResourcePlan.Project(Pool(0, asOf: Noon), Noon.AddDays(3));

        Assert.Equal(GameRules.StaminaCap, outlook.Balance);
        Assert.Equal(1, outlook.WastedSinceFull);   // full after 60h, so 12h wasted of 72
    }

    [Fact]
    public void A_pool_short_of_the_cap_has_wasted_nothing()
    {
        var outlook = ResourcePlan.Project(Pool(2, asOf: Noon), Noon.AddHours(6));

        Assert.Equal(2, outlook.Balance);
        Assert.Equal(0, outlook.WastedSinceFull);
        Assert.Equal(TimeSpan.FromHours(6), outlook.UntilNext);
        // Three more units at 12h each, the first of them 6h away.
        Assert.Equal(TimeSpan.FromHours(30), outlook.UntilFull);
    }

    [Fact]
    public void Sixty_hours_fills_an_empty_pool()
    {
        // The humane-advice number: five units at 12 hours each. It is why a half-full pool needs
        // no attention for days rather than twice-daily check-ins.
        Assert.Equal(TimeSpan.FromHours(60), GameRules.StaminaFullRefill);

        var outlook = ResourcePlan.Project(Pool(0, asOf: Noon), Noon);
        Assert.Equal(TimeSpan.FromHours(60), outlook.UntilFull);
    }

    [Fact]
    public void Takes_an_undated_figure_at_face_value()
    {
        // No timestamp means no known age. Inventing one would invent regeneration.
        var outlook = ResourcePlan.Project(Pool(2), Noon.AddDays(9));

        Assert.Equal(2, outlook.Balance);
        Assert.Equal(0, outlook.WastedSinceFull);
    }

    [Fact]
    public void Twelve_hourglasses_are_worth_one_unit()
    {
        var outlook = ResourcePlan.Project(Pool(1, hourglasses: 30, asOf: Noon), Noon);

        Assert.Equal(2, outlook.FromHourglasses);   // 30 / 12, floored
        Assert.Equal(3, outlook.Reachable);
    }

    [Fact]
    public void Carries_the_raw_hourglass_balance_through_untouched()
    {
        // The projection floors 1,751 hourglasses to 145 restorable units, and 145 times twelve is
        // 1,740 - so a UI that multiplied back would quietly destroy eleven hourglasses on every
        // save. The raw figure has to survive the trip.
        var outlook = ResourcePlan.Project(Pool(1, hourglasses: 1751, asOf: Noon), Noon);

        Assert.Equal(145, outlook.FromHourglasses);
        Assert.Equal(1751, outlook.Hourglasses);
        Assert.NotEqual(outlook.Hourglasses, outlook.FromHourglasses * 12);
    }

    [Fact]
    public void Premium_is_a_fifty_percent_pack_rate_increase()
    {
        var plain = ResourcePlan.Packs(Resources.Empty, Noon);
        var premium = ResourcePlan.Packs(Resources.Empty with { Premium = true }, Noon);

        Assert.Equal(2, plain.PerDay);
        Assert.Equal(3, premium.PerDay);

        // The figure that changes decisions: the same target, two timelines.
        Assert.Equal(120, plain.DaysFor(240));
        Assert.Equal(80, premium.DaysFor(240));
    }

    [Fact]
    public void Pack_hourglasses_shorten_the_timeline_before_the_rate_applies()
    {
        var resources = Resources.Empty with { PackHourglasses = 36 };
        var outlook = ResourcePlan.Packs(resources, Noon);

        Assert.Equal(3, outlook.FromHourglasses);   // 36 / 12
        // Three packs are already paid for, so only the remaining seven wait on the clock.
        Assert.Equal(3.5, outlook.DaysFor(10));
    }

    [Fact]
    public void Spending_takes_the_regenerated_balance_not_the_figure_that_was_typed()
    {
        // Entered empty two days ago, so the pool has filled since. Charging the spend against
        // the nought on file would have taken 24 hourglasses for stamina the user already had.
        var pool = Pool(0, hourglasses: 24, asOf: Noon);
        var spend = ResourcePlan.Spend(pool, 2, Noon.AddDays(2));

        Assert.Equal(2, spend.FromBalance);
        Assert.Equal(0, spend.FromHourglasses);
        Assert.Equal(0, spend.Hourglasses);
        Assert.Equal(24, spend.Pool.Hourglasses);
        // 48 hours is four units of regeneration from empty, and two of those were spent.
        Assert.Equal(2, spend.Pool.Balance);
        Assert.Equal(0, spend.Unpaid);
    }

    [Fact]
    public void Hourglasses_cover_what_the_balance_cannot()
    {
        // A 4-stamina offer against a balance of one. The pick happened, so the other three were
        // paid for -- 36 hourglasses at twelve each.
        var spend = ResourcePlan.Spend(Pool(1, hourglasses: 40, asOf: Noon), 4, Noon);

        Assert.Equal(1, spend.FromBalance);
        Assert.Equal(3, spend.FromHourglasses);
        Assert.Equal(36, spend.Hourglasses);
        Assert.Equal(0, spend.Pool.Balance);
        Assert.Equal(4, spend.Pool.Hourglasses);
        Assert.Equal(0, spend.Unpaid);
        Assert.Equal(Noon, spend.Pool.AsOf);
    }

    [Fact]
    public void A_part_paid_spend_says_how_much_nothing_covered()
    {
        // Eleven hourglasses buy nothing: the game restores stamina twelve at a time. The app
        // records the pick anyway and reports the shortfall rather than inventing stamina.
        var spend = ResourcePlan.Spend(Pool(1, hourglasses: 11, asOf: Noon), 3, Noon);

        Assert.Equal(1, spend.FromBalance);
        Assert.Equal(0, spend.FromHourglasses);
        Assert.Equal(2, spend.Unpaid);
        Assert.Equal(11, spend.Pool.Hourglasses);
    }

    [Fact]
    public void The_days_free_packs_come_before_any_hourglass_is_spent()
    {
        // Three packs on a base account: two free, one bought for twelve.
        var spend = ResourcePlan.PacksOpened(openedToday: 0, opening: 3, hourglasses: 100, premium: false);

        Assert.Equal(2, spend.Free);
        Assert.Equal(1, spend.FromHourglasses);
        Assert.Equal(GameRules.PackHourglassesPerPack, spend.Hourglasses);
        Assert.Equal(0, spend.Unpaid);

        // Premium's third pack is free, so the same three cost nothing.
        var withPremium = ResourcePlan.PacksOpened(0, 3, 100, premium: true);
        Assert.Equal(3, withPremium.Free);
        Assert.Equal(0, withPremium.Hourglasses);
    }

    [Fact]
    public void Packs_already_opened_today_have_used_up_the_allowance()
    {
        // The allowance is a day's, not a batch's: two already logged means this one is bought.
        var spend = ResourcePlan.PacksOpened(openedToday: 2, opening: 1, hourglasses: 12, premium: false);

        Assert.Equal(0, spend.Free);
        Assert.Equal(1, spend.FromHourglasses);
        Assert.Equal(12, spend.Hourglasses);
    }

    [Fact]
    public void Packs_beyond_the_hourglasses_are_reported_rather_than_refused()
    {
        // Twelve packs logged with one pack's worth of hourglasses on file. The app is being told
        // what happened, so it books what it can and says how far behind the balance was.
        var spend = ResourcePlan.PacksOpened(openedToday: 0, opening: 12, hourglasses: 12, premium: false);

        Assert.Equal(2, spend.Free);
        Assert.Equal(1, spend.FromHourglasses);
        Assert.Equal(9, spend.Unpaid);
    }

    [Fact]
    public void Nothing_opened_costs_nothing()
    {
        Assert.Equal(ResourcePlan.PacksOpened(0, 0, 500, false), PackSpend.None);
    }

    [Fact]
    public void Packs_are_counted_against_the_readers_own_day()
    {
        // Local dates, because the free packs reset by calendar day where the user is. An event
        // at 23:30 local belongs to that day even where UTC has already moved on.
        var today = DateTimeOffset.Now;
        var log = new List<PackOpenEvent>
        {
            new(today, "A1", "Charizard", "unknown", []),
            new(today.AddDays(-1), "A1", "Charizard", "unknown", []),
        };

        Assert.Equal(1, ResourcePlan.PacksOpenedOn(log, DateOnly.FromDateTime(DateTime.Now)));
        Assert.Equal(1, ResourcePlan.PacksOpenedOn(log, DateOnly.FromDateTime(DateTime.Now.AddDays(-1))));
        Assert.Equal(0, ResourcePlan.PacksOpenedOn(log, DateOnly.FromDateTime(DateTime.Now.AddDays(1))));
    }

    [Fact]
    public void Dust_runway_counts_trades_not_currency()
    {
        // 25,000 is the 2-star price. The point of the figure is that it is usually far beyond
        // the stamina available, which is what says dust is not the constraint.
        Assert.Equal(3, ResourcePlan.DustRunway(80_000, 25_000));
        Assert.Equal(0, ResourcePlan.DustRunway(1_000, 25_000));

        // A free rarity must not divide by zero and report bankruptcy.
        Assert.Equal(int.MaxValue, ResourcePlan.DustRunway(0, 0));
    }
}
