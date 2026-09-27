namespace Oaza.Domain.Services;

/// <summary>
/// Splits a money amount among recipients in proportion to their weights,
/// rounded to whole haléře (0.01 CZK) by the largest-remainder method, so the
/// parts always sum exactly to the total. Shared by every new cost calculation
/// (components, losses, credits, interim closings).
/// </summary>
public static class Allocator
{
    private const decimal Cent = 0.01m;

    /// <summary>
    /// Allocates <paramref name="total"/> by <paramref name="weights"/>.
    /// Each part is first truncated to haléře; the haléře left over go one by one
    /// to the parts with the largest truncated remainder. Equal remainders are
    /// resolved deterministically in favour of the lower index. A negative total
    /// (a credit) is allocated by its magnitude and negated, so it mirrors the
    /// positive case exactly.
    /// </summary>
    /// <param name="total">Amount in CZK with at most two decimal places.</param>
    /// <param name="weights">Non-negative weights; at least one must be positive.</param>
    /// <returns>One part per weight, in the same order; Σ parts = total.</returns>
    public static decimal[] Allocate(decimal total, IReadOnlyList<decimal> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count == 0)
            throw new ArgumentException("At least one weight is required.", nameof(weights));
        if (weights.Any(w => w < 0))
            throw new ArgumentException("Weights must not be negative.", nameof(weights));
        if (decimal.Round(total, 2) != total)
            throw new ArgumentException("Total must be a whole number of haléře (at most 2 decimal places).", nameof(total));

        var weightSum = weights.Sum();
        if (weightSum <= 0)
            throw new ArgumentException("At least one weight must be positive.", nameof(weights));

        var sign = total < 0 ? -1m : 1m;
        var magnitude = Math.Abs(total);

        var parts = new decimal[weights.Count];
        var remainders = new decimal[weights.Count];
        for (var i = 0; i < weights.Count; i++)
        {
            var exact = magnitude * weights[i] / weightSum;
            parts[i] = decimal.Truncate(exact / Cent) * Cent;
            remainders[i] = exact - parts[i];
        }

        var leftoverCents = (int)((magnitude - parts.Sum()) / Cent);
        var order = Enumerable.Range(0, weights.Count)
            .Where(i => weights[i] > 0) // a zero weight never receives a haléř
            .OrderByDescending(i => remainders[i])
            .ThenBy(i => i)
            .ToList();
        for (var k = 0; k < leftoverCents; k++)
        {
            parts[order[k % order.Count]] += Cent;
        }

        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] *= sign;
        }

        return parts;
    }

    /// <summary>Keyed variant: allocates by the weight of each key and returns the part per key.</summary>
    public static IReadOnlyDictionary<TKey, decimal> Allocate<TKey>(decimal total, IReadOnlyList<KeyValuePair<TKey, decimal>> weights)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(weights);
        var parts = Allocate(total, weights.Select(w => w.Value).ToList());
        return weights
            .Select((w, i) => (w.Key, Part: parts[i]))
            .ToDictionary(x => x.Key, x => x.Part);
    }
}
