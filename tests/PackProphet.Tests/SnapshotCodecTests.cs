namespace PackProphet.Tests;

using PackProphet.State;

public class SnapshotCodecTests
{
    [Fact]
    public void RoundTrips_name_collection_and_targets()
    {
        var profile = Profile.NewDefault("id", "My collection") with
        {
            Collection = new Dictionary<string, int> { ["a.webp"] = 2, ["b.webp"] = 1 },
            Targets = new TargetSettings(
                new Dictionary<int, int> { [0] = 2, [1] = 1 },
                new Dictionary<string, Dictionary<int, int>> { ["A1"] = new() { [3] = 1 } }),
        };

        var code = SnapshotCodec.Encode(profile);
        var snapshot = SnapshotCodec.TryDecode(code);

        Assert.NotNull(snapshot);
        Assert.Equal(profile.Name, snapshot!.Name);
        Assert.Equal(profile.Collection, snapshot.Collection);
        Assert.Equal(profile.Targets.DefaultPlan, snapshot.Targets.DefaultPlan);
        Assert.Equal(profile.Targets.PlanBySet["A1"], snapshot.Targets.PlanBySet["A1"]);
    }

    [Fact]
    public void An_empty_collection_still_round_trips()
    {
        var profile = Profile.NewDefault();
        var snapshot = SnapshotCodec.TryDecode(SnapshotCodec.Encode(profile));

        Assert.NotNull(snapshot);
        Assert.Empty(snapshot!.Collection);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 at all!!")]
    [InlineData("dGhpcyBpcyBub3QgY29tcHJlc3NlZCBqc29u")] // valid base64, garbage once inflated
    public void Garbage_input_decodes_to_null_rather_than_throwing(string? code)
    {
        Assert.Null(SnapshotCodec.TryDecode(code));
    }

    [Fact]
    public void The_code_is_URL_safe()
    {
        // Pasted into a query string with no escaping, so it must contain none of the characters
        // that would need it.
        var profile = Profile.NewDefault("id", "A/B+C") with
        {
            Collection = Enumerable.Range(0, 50)
                .ToDictionary(i => $"card-{i}.webp", i => i % 3),
        };

        var code = SnapshotCodec.Encode(profile);

        Assert.DoesNotContain('+', code);
        Assert.DoesNotContain('/', code);
        Assert.DoesNotContain('=', code);
    }

    [Fact]
    public void A_real_sized_collection_compresses_well()
    {
        var profile = Profile.NewDefault() with
        {
            Collection = Enumerable.Range(0, 2000)
                .ToDictionary(i => $"A1:card-{i:0000}.webp", i => 1 + i % 2),
        };

        var code = SnapshotCodec.Encode(profile);
        var uncompressed = Convert.ToBase64String(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new SharedSnapshot(profile.Name, profile.Collection, profile.Targets)));

        // Not a tight bound, just a guard against silently losing the compression step, which
        // would make the deflate/base64url plumbing pure overhead on every link.
        Assert.True(
            code.Length < uncompressed.Length * 0.6,
            $"expected the deflate step to meaningfully shrink the link: {code.Length} vs {uncompressed.Length} uncompressed");
    }
}
