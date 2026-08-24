using PackProphet.Engine;
using Need = PackProphet.Engine.CompletionEstimator.Need;

namespace PackProphet.Tests;

/// <summary>
/// The integral has closed-form answers in several special cases, which is what the numerics are
/// checked against. A subtly wrong estimator produces plausible numbers rather than an error.
/// </summary>
public class CompletionEstimatorTests
{
    /// <summary>Relative-tolerance comparison: absolute tolerance is meaningless when the
    /// expected values span 1/0.5 to 1/0.00002.</summary>
    private static void AssertClose(double expected, double actual, double relativeTolerance)
    {
        var error = Math.Abs(actual - expected) / Math.Abs(expected);
        Assert.True(error <= relativeTolerance,
            $"expected {expected}, got {actual} (relative error {error:E3} > {relativeTolerance:E3})");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.05)]
    [InlineData(0.001)]
    public void OneNeed_OneCopy_IsExactlyOneOverRate(double rate)
    {
        // E[Exp(rate)] = 1/rate.
        var e = CompletionEstimator.ExpectedPacks([new Need(rate, 1)]);
        AssertClose(1.0 / rate, e, 1e-6);
    }

    [Theory]
    [InlineData(1, 0.02)]
    [InlineData(2, 0.02)]
    [InlineData(5, 0.01)]
    [InlineData(10, 0.5)]
    public void OneNeed_kCopies_IsErlangMean_kOverRate(int copies, double rate)
    {
        // Waiting for the k-th arrival of a Poisson process is Erlang(k, rate), mean k/rate.
        // This is the generalisation the single-copy trackers cannot express.
        var e = CompletionEstimator.ExpectedPacks([new Need(rate, copies)]);
        AssertClose(copies / rate, e, 1e-6);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(50)]
    public void ManyEqualNeeds_MatchTheCouponCollectorHarmonicSum(int n)
    {
        // E[max of n iid Exp(rate)] = H_n / rate. The classic coupon-collector result, and
        // the exact shape of "collect one of each card at equal odds".
        const double rate = 0.01;
        var harmonic = Enumerable.Range(1, n).Sum(i => 1.0 / i);

        var e = CompletionEstimator.ExpectedPacks(
            Enumerable.Repeat(new Need(rate, 1), n).ToArray());

        AssertClose(harmonic / rate, e, 1e-5);
    }

    [Fact]
    public void TwoUnequalNeeds_MatchTheClosedFormForMaxOfTwoExponentials()
    {
        // E[max(X,Y)] = 1/a + 1/b - 1/(a+b) for independent Exp(a), Exp(b).
        const double a = 0.03, b = 0.005;
        var expected = 1 / a + 1 / b - 1 / (a + b);

        var e = CompletionEstimator.ExpectedPacks([new Need(a, 1), new Need(b, 1)]);

        AssertClose(expected, e, 1e-6);
    }

    [Fact]
    public void NothingOutstanding_IsZeroPacks()
    {
        Assert.Equal(0.0, CompletionEstimator.ExpectedPacks([]));
        Assert.Equal(0.0, CompletionEstimator.ExpectedPacks([new Need(0.1, 0)]));
    }

    [Fact]
    public void AnUnobtainableNeed_IsInfinite_NotMerelyLarge()
    {
        // "Never" must stay distinguishable from "eventually". A card with no source has
        // to surface as impossible, not as a big number the user might try to grind out.
        var e = CompletionEstimator.ExpectedPacks([new Need(0.05, 1), new Need(0.0, 1)]);
        Assert.True(double.IsPositiveInfinity(e));
    }

    [Fact]
    public void MoreCopiesNeeded_NeverReducesTheEstimate()
    {
        var one = CompletionEstimator.ExpectedPacks([new Need(0.01, 1)]);
        var two = CompletionEstimator.ExpectedPacks([new Need(0.01, 2)]);
        var three = CompletionEstimator.ExpectedPacks([new Need(0.01, 3)]);

        Assert.True(one < two && two < three);
    }

    [Fact]
    public void HigherRate_NeverIncreasesTheEstimate()
    {
        var slow = CompletionEstimator.ExpectedPacks([new Need(0.001, 1)]);
        var fast = CompletionEstimator.ExpectedPacks([new Need(0.1, 1)]);
        Assert.True(fast < slow);
    }

    [Fact]
    public void ScalesToARealisticCollectionSize_WithoutUnderflowing()
    {
        // ~3,000 demands at genuinely tiny rates is the actual workload. The product of
        // that many sub-1 factors underflows a naive implementation to zero, which would
        // silently report "already complete".
        var needs = Enumerable.Range(0, 3000)
            .Select(i => new Need(0.00002 + i * 1e-7, i % 4 == 0 ? 2 : 1))
            .ToArray();

        var e = CompletionEstimator.ExpectedPacks(needs);

        Assert.True(e > 0 && double.IsFinite(e), $"expected a finite positive estimate, got {e}");
        // Must exceed the slowest single need on its own; the max of many is never smaller.
        Assert.True(e > 1.0 / 0.00002);
    }

    [Theory]
    [InlineData(0.0, 1, 0.0)]
    [InlineData(1.0, 0, 1.0)]
    public void PoissonTail_HandlesDegenerateInputs(double lambda, int k, double expected) =>
        Assert.Equal(expected, CompletionEstimator.PoissonTail(lambda, k));

    [Fact]
    public void PoissonTail_MatchesHandComputedValues()
    {
        // P(N>=1) = 1-e^-1; P(N>=2) = 1-e^-1(1+1)
        Assert.Equal(1 - Math.Exp(-1), CompletionEstimator.PoissonTail(1.0, 1), 1e-12);
        Assert.Equal(1 - 2 * Math.Exp(-1), CompletionEstimator.PoissonTail(1.0, 2), 1e-12);
    }
}
