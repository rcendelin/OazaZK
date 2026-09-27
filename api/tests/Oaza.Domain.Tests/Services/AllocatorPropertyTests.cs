using FsCheck;
using FsCheck.Xunit;
using Oaza.Domain.Services;

namespace Oaza.Domain.Tests.Services;

/// <summary>
/// Property-based tests (X6): invariants of <see cref="Allocator"/> checked on
/// randomly generated totals and weights instead of hand-picked examples.
/// Weights are built as one positive weight plus any number of weights 0–1000
/// (zeros included), shuffled by FsCheck's own generation order.
/// </summary>
public class AllocatorPropertyTests
{
    private const decimal Cent = 0.01m;

    /// <summary>Total in haléře, ± 10 000 000 Kč.</summary>
    private static decimal Total(int cents) => cents % 1_000_000_000 * Cent;

    private static decimal[] Weights(PositiveInt first, NonNegativeInt[] rest) =>
        rest.Select(w => (decimal)(w.Get % 1001))
            .Prepend(first.Get % 1000 + 1)
            .ToArray();

    [Property(MaxTest = 500)]
    public bool PartsAlwaysSumToTotal(int cents, PositiveInt first, NonNegativeInt[] rest)
    {
        var total = Total(cents);
        return Allocator.Allocate(total, Weights(first, rest)).Sum() == total;
    }

    [Property(MaxTest = 500)]
    public bool EveryPartIsWholeHalereAndWithinOneHalerOfExactShare(int cents, PositiveInt first, NonNegativeInt[] rest)
    {
        var total = Total(cents);
        var weights = Weights(first, rest);
        var sum = weights.Sum();
        return Allocator.Allocate(total, weights)
            .Select((part, i) => (part, exact: total * weights[i] / sum))
            .All(x => decimal.Round(x.part, 2) == x.part && Math.Abs(x.part - x.exact) < Cent);
    }

    [Property(MaxTest = 500)]
    public bool ZeroWeightNeverReceivesAnything(int cents, PositiveInt first, NonNegativeInt[] rest)
    {
        var weights = Weights(first, rest);
        var parts = Allocator.Allocate(Total(cents), weights);
        return weights.Select((w, i) => w != 0 || parts[i] == 0).All(ok => ok);
    }

    [Property(MaxTest = 500)]
    public bool NegativeTotalMirrorsPositive(int cents, PositiveInt first, NonNegativeInt[] rest)
    {
        var weights = Weights(first, rest);
        var total = Math.Abs(Total(cents));
        return Allocator.Allocate(-total, weights)
            .SequenceEqual(Allocator.Allocate(total, weights).Select(p => -p));
    }

}
