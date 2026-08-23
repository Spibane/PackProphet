namespace PackProphet.Engine;

using PackProphet.Data;
using PackProphet.Domain;

/// <summary>
/// One unit of outstanding demand: how many more copies are needed, and which cards can
/// supply it.
///
/// Demand carries a QUANTITY rather than being mere set membership. That is the whole
/// point: a set cannot express "this deck needs two Pikachu ex", nor "I want two of every
/// diamond", which is what most players actually want.
/// </summary>
/// <param name="Key">Stable identity for grouping and display.</param>
/// <param name="Remaining">Copies still needed. Always &gt; 0 for a live demand.</param>
/// <param name="SuppliedBy">
/// Every card that satisfies this demand. For collection demand that is a single printing;
/// for deck demand it is every printing of one card identity, whose rates pool.
/// </param>
public readonly record struct Demand(string Key, int Remaining, IReadOnlyList<PocketCard> SuppliedBy);

/// <summary>A definition of "done". Every ranking and estimate consumes one of these.</summary>
public interface ICompletionTarget
{
    /// <summary>Human label for the UI, e.g. "Genetic Apex — all diamonds, 2 copies".</summary>
    string Describe();

    /// <summary>
    /// Outstanding demand given what is already owned. Satisfied demands are omitted, so an
    /// empty result means the target is complete.
    /// </summary>
    IReadOnlyList<Demand> Outstanding(CardIndex index, Collection owned);
}
