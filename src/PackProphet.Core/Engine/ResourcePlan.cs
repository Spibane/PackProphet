namespace PackProphet.Engine;

using PackProphet.Domain;
using PackProphet.State;

/// <param name="Balance">Units held now, projected forward from when the figure was entered.</param>
/// <param name="Entered">What the user actually typed, kept so the projection can be shown as one.</param>
/// <param name="WastedSinceFull">
/// Units of regeneration lost to sitting at the cap. The single most actionable number here: a
/// full pool is not a saved pool, it is a stopped one.
/// </param>
/// <param name="FromHourglasses">Units the hourglass balance could restore right now.</param>
/// <param name="Hourglasses">
/// The raw hourglass balance, carried through unchanged. FromHourglasses divides it by twelve and
/// floors, so it cannot be multiplied back: 1,751 hourglasses is 145 units, and 145 times twelve
/// is 1,740. Anything showing the balance back to the user must read this.
/// </param>
public sealed record PoolOutlook(
    int Balance,
    int Entered,
    int Cap,
    TimeSpan? UntilNext,
    TimeSpan? UntilFull,
    int WastedSinceFull,
    int FromHourglasses,
    int Hourglasses = 0)
{
    public bool AtCap => Balance >= Cap;

    /// <summary>Everything spendable if the hourglasses are cashed in too.</summary>
    public int Reachable => Balance + FromHourglasses;
}

/// <param name="PerDay">Free packs a day, which the premium membership changes by half again.</param>
/// <param name="FromHourglasses">Packs the hourglass balance could bring forward now.</param>
/// <param name="UntilNextFree">Null when the next free pack is already waiting, or unknown.</param>
public sealed record PackOutlook(
    double PerDay,
    int FromHourglasses,
    TimeSpan? UntilNextFree,
    bool Premium)
{
    /// <summary>
    /// Days to open <paramref name="packs"/> at this rate, hourglasses included. The figure that
    /// turns "1,900 packs" into a decision.
    /// </summary>
    public double DaysFor(double packs) =>
        PerDay <= 0 ? double.PositiveInfinity : Math.Max(0, packs - FromHourglasses) / PerDay;
}

/// <summary>
/// Projects the three resource systems forward from what the user last told us.
///
/// The systems are Pack, Wonder and Trade, and NOTHING converts between them: three separate
/// hourglass currencies, three separate pools. So this returns three separate outlooks and never
/// a total. Wonder and Trade happen to be structurally identical - cap 5, one unit per 12 hours -
/// which is why one function serves both rather than each having its own.
///
/// Everything hinges on the fact that a capped pool STOPS REGENERATING. Sitting at five stamina
/// is not thrift, it is loss, and the size of that loss is computable: every 12 hours spent full
/// is a unit nobody will ever get back. Equally, a pool at two of five has 36 hours of slack
/// before it wastes anything, which is a reason NOT to nag - the advice is "no rush until
/// Thursday", not "check back twice a day".
/// </summary>
public static class ResourcePlan
{
    /// <summary>
    /// Where a stamina pool stands now, given what it held when it was last entered.
    ///
    /// An absent timestamp means the figure has no age, so it is taken at face value: guessing
    /// how long ago it was typed would invent regeneration that may not have happened.
    /// </summary>
    public static PoolOutlook Project(
        ResourcePool pool,
        DateTimeOffset now,
        int cap = GameRules.StaminaCap,
        int hourglassesPerUnit = GameRules.WonderHourglassesPerStamina)
    {
        var entered = Math.Clamp(pool.Balance, 0, cap);
        var regen = GameRules.StaminaRegen;

        var elapsed = pool.AsOf is { } asOf && now > asOf ? now - asOf : TimeSpan.Zero;
        var gained = (int)(elapsed.Ticks / regen.Ticks);

        var balance = Math.Min(cap, entered + gained);

        // Regeneration lost to the cap. Only counted from the moment the pool actually filled,
        // which is `cap - entered` units after it was entered - not from when it was entered, or
        // a pool entered at one of five would be reported as having wasted days it spent filling.
        var wasted = 0;
        if (balance >= cap && pool.AsOf is not null)
        {
            var filledAfter = regen * (cap - entered);
            var full = elapsed - filledAfter;
            if (full > TimeSpan.Zero) wasted = (int)(full.Ticks / regen.Ticks);
        }

        // Time to the next unit, measured from the last one to arrive rather than from now, or a
        // pool checked twice in a row would keep restarting its own clock.
        TimeSpan? untilNext = balance >= cap
            ? null
            : regen - TimeSpan.FromTicks(elapsed.Ticks % regen.Ticks);

        TimeSpan? untilFull = balance >= cap
            ? null
            : untilNext + regen * (cap - balance - 1);

        var fromHourglasses = hourglassesPerUnit <= 0
            ? 0
            : Math.Max(0, pool.Hourglasses) / hourglassesPerUnit;

        return new PoolOutlook(balance, entered, cap, untilNext, untilFull, wasted, fromHourglasses,
                               Math.Max(0, pool.Hourglasses));
    }

    /// <summary>
    /// The pack side, which is shaped differently from the stamina pools: packs are not stored, so
    /// there is no pool and no cap - only a rate, and the hourglasses that bring the next one
    /// forward.
    /// </summary>
    public static PackOutlook Packs(Resources resources, DateTimeOffset now)
    {
        var until = resources.NextFreePackAt is { } next && next > now
            ? next - now
            : (TimeSpan?)null;

        return new PackOutlook(
            GameRules.PacksPerDay(resources.Premium),
            Math.Max(0, resources.PackHourglasses) / GameRules.PackHourglassesPerPack,
            until,
            resources.Premium);
    }

    /// <summary>
    /// Trades the dust balance can fund at a given price, which is the only figure that makes
    /// dust worth showing.
    ///
    /// Stamina caps trading at roughly two a day while dust merely accumulates, so dust is
    /// usually NOT the binding constraint. A runway far beyond the stamina you could earn in the
    /// same period is the app's cue to stop presenting dust as a cost at all.
    /// </summary>
    public static int DustRunway(int shinedust, int pricePerTrade) =>
        pricePerTrade <= 0 ? int.MaxValue : Math.Max(0, shinedust) / pricePerTrade;
}
