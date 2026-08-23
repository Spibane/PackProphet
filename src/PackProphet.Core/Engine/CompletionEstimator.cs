namespace PackProphet.Engine;

/// <summary>
/// Expected packs to satisfy a set of demands, via the Poissonization identity.
///
/// For demand u needing k_u more copies, arriving at λ_u copies per pack, the chance it is
/// satisfied after t packs is a Poisson tail. The expected time until ALL demands are met
/// is then a single integral:
///
///     P_u(t) = P(Poisson(λ_u·t) ≥ k_u)
///     E      = ∫₀^∞ ( 1 − Π_u P_u(t) ) dt
///
/// At k = 1 this collapses to 1 − e^(−λt), the single-copy formula that every other
/// tracker implements — so multi-copy support is the same integral, not a different
/// algorithm.
///
/// Exact in the continuous-time limit. Treating a pack's slots as independent introduces
/// error of order λ², negligible at real pull rates.
/// </summary>
public static class CompletionEstimator
{
    /// <summary>A demand reduced to what the integral needs: an arrival rate and a count.</summary>
    public readonly record struct Need(double Rate, int Copies);

    // 8-point Gauss-Legendre on [-1,1]; exact for polynomials to degree 15. Ample here,
    // since the integrand is smooth within each panel.
    private static readonly double[] GlNodes =
    [
        -0.9602898564975363, -0.7966664774136267, -0.5255324099163290, -0.1834346424956498,
         0.1834346424956498,  0.5255324099163290,  0.7966664774136267,  0.9602898564975363
    ];
    private static readonly double[] GlWeights =
    [
        0.1012285362903763, 0.2223810344533745, 0.3137066458778873, 0.3626837833783620,
        0.3626837833783620, 0.3137066458778873, 0.2223810344533745, 0.1012285362903763
    ];

    /// <summary>
    /// Expected packs to satisfy every need. Returns 0 when nothing is outstanding, and
    /// +Infinity when any need can never be met (rate 0) — an honest "never", which the UI
    /// must render as such rather than as a very large number.
    /// </summary>
    public static double ExpectedPacks(IReadOnlyCollection<Need> needs, int panels = 96)
    {
        var live = needs.Where(n => n.Copies > 0).ToArray();
        if (live.Length == 0) return 0.0;
        if (live.Any(n => n.Rate <= 0)) return double.PositiveInfinity;

        // Identical (rate, copies) pairs are extremely common: every card on the same
        // rarity rung in the same pack shares a rate. Collapsing them to multiplicities
        // turns thousands of log() calls per integration node into a handful.
        var grouped = live
            .GroupBy(n => (Rate: Math.Round(n.Rate, 15), n.Copies))
            .Select(g => (g.Key.Rate, g.Key.Copies, Multiplicity: g.Count()))
            .ToArray();

        var upper = FindUpperBound(grouped);
        if (double.IsPositiveInfinity(upper)) return double.PositiveInfinity;

        // Composite Gauss-Legendre. The integrand falls from 1 towards 0 over a range set
        // by the slowest need, so uniform panels across [0, upper] are well-behaved.
        var total = 0.0;
        var h = upper / panels;
        for (var p = 0; p < panels; p++)
        {
            var a = p * h;
            var mid = a + h / 2.0;
            var half = h / 2.0;
            for (var i = 0; i < GlNodes.Length; i++)
                total += GlWeights[i] * Integrand(mid + half * GlNodes[i], grouped);
        }
        return total * h / 2.0;
    }

    /// <summary>1 − Π P_u(t): the chance at least one demand is still outstanding at t.</summary>
    private static double Integrand(double t, (double Rate, int Copies, int Multiplicity)[] needs)
    {
        // Accumulate in log space: with thousands of factors each below 1, a direct
        // product underflows to zero long before the integrand actually vanishes.
        var logProduct = 0.0;
        foreach (var (rate, copies, mult) in needs)
        {
            var p = PoissonTail(rate * t, copies);
            if (p <= 0) return 1.0;                 // certainly unsatisfied
            logProduct += mult * Math.Log(p);
            if (logProduct < -745) return 1.0;      // exp() would underflow anyway
        }
        return 1.0 - Math.Exp(logProduct);
    }

    /// <summary>P(Poisson(lambda) &gt;= k), computed by summing the small lower tail.</summary>
    internal static double PoissonTail(double lambda, int k)
    {
        if (k <= 0) return 1.0;
        if (lambda <= 0) return 0.0;

        // k is 1 or 2 in practice (the deck copy limit is 2), so the direct sum of the
        // lower tail is both exact and cheap.
        var term = Math.Exp(-lambda);   // j = 0
        var cumulative = term;
        for (var j = 1; j < k; j++)
        {
            term *= lambda / j;
            cumulative += term;
        }
        return Math.Clamp(1.0 - cumulative, 0.0, 1.0);
    }

    /// <summary>
    /// Grow t until the chance of anything outstanding is negligible, so the truncated
    /// integral loses nothing that matters.
    /// </summary>
    private static double FindUpperBound((double Rate, int Copies, int Multiplicity)[] needs)
    {
        var slowest = needs.Min(n => n.Rate);
        if (slowest <= 0) return double.PositiveInfinity;

        var t = Math.Max(1.0, 1.0 / slowest);
        for (var i = 0; i < 200; i++)
        {
            if (Integrand(t, needs) < 1e-12) return t;
            t *= 1.6;
        }
        return t;
    }
}
