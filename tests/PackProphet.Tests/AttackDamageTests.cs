using PackProphet.Data;

namespace PackProphet.Tests;

public class AttackDamageTests
{
    private static CardAttack Attack(int? damage, string? effect) =>
        new() { Name = "Attack", Cost = "C", Damage = damage, Effect = effect };

    [Theory]
    // Multipliers — printed with a ×, and the reason this exists: a bare "50" understates
    // Pinsir's Double Horn by up to 100.
    [InlineData(50, "Flip 2 coins. This attack does 50 damage for each heads.", "50×")]
    [InlineData(30, "This attack does 30 damage for each of your Benched [L] Pokémon.", "30×")]
    // Conditional bonuses — printed with a +.
    [InlineData(30, "Flip a coin. If heads, this attack does 30 more damage.", "30+")]
    [InlineData(80, "If this Pokémon has at least 2 extra [W] Energy attached, this attack does 60 more damage.", "80+")]
    // Effects that mention damage without modifying this attack's.
    [InlineData(80, "Heal 30 damage from this Pokémon.", "80")]
    [InlineData(100, "This Pokémon also does 20 damage to itself.", "100")]
    // "to each" hits every Bench member for a fixed amount; the card prints a plain number.
    [InlineData(30, "This attack does 30 damage to each of your opponent's Benched Pokémon.", "30")]
    [InlineData(60, null, "60")]
    [InlineData(0, "", "0")]
    public void LabelsDamageAsThePrintedCardDoes(int damage, string? effect, string expected) =>
        Assert.Equal(expected, Attack(damage, effect).DamageLabel);

    [Fact]
    public void NoDamageMeansNoLabel()
    {
        // A status-only attack shows nothing rather than a "0" that reads like a whiffed hit.
        Assert.Equal("", Attack(null, "Your opponent's Active Pokémon is now Asleep.").DamageLabel);
    }

    /// <summary>
    /// The patterns are heuristics over English prose, so they are held against the real data:
    /// both buckets must be populated, and they must not overlap.
    /// </summary>
    [Fact]
    public void OverTheRealDataset_BucketsArePopulatedAndDisjoint()
    {
        var attacks = Snapshot.Facts().All
            .SelectMany(f => f.AttackList)
            .Where(a => a.Damage is not null)
            .ToArray();

        var multipliers = attacks.Count(a => a.DamageLabel.EndsWith('×'));
        var bonuses = attacks.Count(a => a.DamageLabel.EndsWith('+'));

        // Measured at 99 and 264 of 1,912 damaging attacks across the 2,244 card identities.
        // Bounded on both sides: a pattern that stopped matching and one that started matching
        // everything are both regressions, and only a range catches the second.
        Assert.InRange(multipliers, 60, 200);
        Assert.InRange(bonuses, 180, 450);

        // An attack cannot be both, so a label carrying two markers means the patterns overlap.
        Assert.DoesNotContain(attacks, a => a.DamageLabel.Contains('×') && a.DamageLabel.Contains('+'));

        // Every label still starts with the base number, whatever suffix it carries.
        Assert.All(attacks, a => Assert.StartsWith(a.Damage!.Value.ToString(), a.DamageLabel));
    }

    [Fact]
    public void KnownCardsMatchTheirPrintedDamage()
    {
        string Label(string card, string attack) =>
            Snapshot.Facts().All
                .Where(f => f.Name == card)
                .SelectMany(f => f.AttackList)
                .First(a => a.Name == attack)
                .DamageLabel;

        Assert.Equal("50×", Label("Pinsir", "Double Horn"));
        Assert.Equal("30×", Label("Pikachu ex", "Circle Circuit"));
        Assert.Equal("30+", Label("Exeggutor", "Stomp"));
        Assert.Equal("80+", Label("Blastoise", "Hydro Pump"));
    }
}
