namespace PackProphet.Engine;

using PackProphet.Domain;
using PackProphet.State;

/// <param name="Balance">Units held now, projected forward from when the figure was entered.</param>
/// <param name="Entered">What the user actually typed, kept so the projection can be shown as one.</param>
/// <param name="WastedSinceFull">
/// Units of regeneration lost to sitting at the cap. A full pool has stopped regenerating.
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
    /// Days to open <paramref name="packs"/> at this rate, hourglasses included.
    /// </summary>
    public double DaysFor(double packs) =>
        PerDay <= 0 ? double.PositiveInfinity : Math.Max(0, packs - FromHourglasses) / PerDay;
}

/// <param name="Pool">The pool after the spend, restamped, so the projection carries on from here.</param>
/// <param name="FromBalance">Units the pool itself covered.</param>
/// <param name="FromHourglasses">Units bought with hourglasses, because the pool was short.</param>
/// <param name="Hourglasses">Hourglasses that cost.</param>
/// <param name="Unpaid">
/// Units neither the pool nor the hourglasses could cover. The app records what the user says
/// happened rather than refusing it, so this is how far behind the stored figures were.
/// </param>
public sealed record PoolSpend(
    ResourcePool Pool,
    int FromBalance,
    int FromHourglasses,
    int Hourglasses,
    int Unpaid);

/// <param name="Free">Packs the day's allowance covered.</param>
/// <param name="FromHourglasses">Packs the hourglasses paid for.</param>
/// <param name="Hourglasses">Hourglasses that cost.</param>
/// <param name="Unpaid">
/// Packs beyond both, which happens when the balance on file is behind what has been opened.
/// </param>
public sealed record PackSpend(int Free, int FromHourglasses, int Hourglasses, int Unpaid)
{
    public static readonly PackSpend None = new(0, 0, 0, 0);
}

/// <summary>
/// Projects the three resource systems forward from what the user last told us.
///
/// The systems are Pack, Wonder and Trade, and nothing converts between them: three separate
/// hourglass currencies, three separate pools. So this returns three separate outlooks and never a
/// total. Wonder and Trade are structurally identical — cap 5, one unit per 12 hours — so one
/// function serves both.
///
/// A capped pool stops regenerating, and the size of that loss is computable: every 12 hours spent
/// full is a unit that cannot be recovered. A pool at two of five has 36 hours of slack before it
/// wastes anything, which the outlook reports as slack rather than as a warning.
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
    /// Spend from a pool, with the hourglasses covering whatever the balance cannot.
    ///
    /// The spend comes off the PROJECTED balance rather than the stored one, which is the whole
    /// reason this is here: a balance entered yesterday has regenerated since, and taking the cost
    /// off the figure as typed would charge hourglasses for stamina the user already had.
    ///
    /// Hourglasses are only drawn in whole units - twelve of them or the stamina does not arrive -
    /// so a balance of eleven pays for nothing and stays where it is.
    /// </summary>
    public static PoolSpend Spend(
        ResourcePool pool,
        int units,
        DateTimeOffset now,
        int cap = GameRules.StaminaCap,
        int hourglassesPerUnit = GameRules.WonderHourglassesPerStamina)
    {
        var outlook = Project(pool, now, cap, hourglassesPerUnit);
        var owed = Math.Max(0, units);

        var fromBalance = Math.Min(owed, outlook.Balance);
        var uncovered = owed - fromBalance;

        // FromHourglasses off the outlook, not the raw balance divided here: one place decides how
        // many units a pile of hourglasses is worth.
        var bought = Math.Min(uncovered, outlook.FromHourglasses);
        var spent = bought * Math.Max(0, hourglassesPerUnit);

        // Restamped even when nothing was spent, because the projected balance is what is being
        // stored: leaving the old timestamp on it would add the same regeneration a second time.
        var after = new ResourcePool(
            outlook.Balance - fromBalance,
            outlook.Hourglasses - spent).AsOfNow(now);

        return new PoolSpend(after, fromBalance, bought, spent, uncovered - bought);
    }

    /// <summary>
    /// What opening packs costs in hourglasses, once the day's free ones are used up.
    ///
    /// Packs are not a pool: there is no balance to draw down, only an allowance that arrives
    /// daily, so the question is how many of these packs the day had left and what the rest cost.
    /// Twelve hourglasses a pack, in whole packs only.
    /// </summary>
    /// <param name="openedToday">Packs already logged today, before these ones.</param>
    /// <param name="opening">Packs being logged now.</param>
    public static PackSpend PacksOpened(int openedToday, int opening, int hourglasses, bool premium)
    {
        var packs = Math.Max(0, opening);
        if (packs == 0) return PackSpend.None;

        var left = Math.Max(0, GameRules.PacksPerDay(premium) - Math.Max(0, openedToday));
        var free = Math.Min(packs, left);
        var owed = packs - free;

        var affordable = Math.Max(0, hourglasses) / GameRules.PackHourglassesPerPack;
        var bought = Math.Min(owed, affordable);

        return new PackSpend(free, bought, bought * GameRules.PackHourglassesPerPack, owed - bought);
    }

    /// <summary>
    /// Packs logged on a given day, in the reader's own time zone: the allowance resets by
    /// calendar day where the user is, not at UTC midnight.
    /// </summary>
    public static int PacksOpenedOn(IEnumerable<PackOpenEvent> log, DateOnly day) =>
        log.Count(e => DateOnly.FromDateTime(e.At.ToLocalTime().DateTime) == day);

    /// <summary>
    /// Trades the dust balance can fund at a given price.
    ///
    /// Stamina caps trading at roughly two a day while dust accumulates, so dust is usually not the
    /// binding constraint. A runway far beyond the stamina earnable in the same period is the cue
    /// to stop presenting dust as a cost.
    /// </summary>
    public static int DustRunway(int shinedust, int pricePerTrade) =>
        pricePerTrade <= 0 ? int.MaxValue : Math.Max(0, shinedust) / pricePerTrade;
}
