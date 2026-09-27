using System.Globalization;
using Oaza.Domain.Entities;

namespace Oaza.Domain.Services;

public enum ReadingEstimateMethod
{
    /// <summary>A physical reading exists on the target date.</summary>
    Exact,

    /// <summary>Linear interpolation by days between the nearest readings before and after.</summary>
    Interpolated,

    /// <summary>Only earlier readings exist — the nearest one is used as is (no extrapolation).</summary>
    NearestBefore,

    /// <summary>Only later readings exist — the nearest one is used as is (no extrapolation).</summary>
    NearestAfter,

    /// <summary>The meter has no readings.</summary>
    None,
}

/// <param name="Value">Meter state in m³, 3 decimals; null for <see cref="ReadingEstimateMethod.None"/>.</param>
/// <param name="IsEstimate">False only for <see cref="ReadingEstimateMethod.Exact"/>.</param>
/// <param name="Note">Human-readable description of the method and source readings (Czech, stored as <c>EstimateNote</c>).</param>
public record ReadingEstimate(decimal? Value, ReadingEstimateMethod Method, bool IsEstimate, string Note);

/// <summary>
/// Estimates a meter state at a calendar date from its readings (T04), e.g. an
/// opening reading when a house changes owner. Works on whole days; the result
/// is rounded to 3 decimal places (0.001 m³ = 1 litre).
/// </summary>
public static class ReadingEstimator
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    public static ReadingEstimate Estimate(IEnumerable<MeterReading> readings, DateTime targetDate)
    {
        ArgumentNullException.ThrowIfNull(readings);
        var target = targetDate.Date;
        var ordered = readings.OrderBy(r => r.ReadingDate).ToList();

        var exact = ordered.LastOrDefault(r => r.ReadingDate.Date == target);
        if (exact is not null)
        {
            return new ReadingEstimate(Round(exact.Value), ReadingEstimateMethod.Exact, false,
                $"Skutečný odečet ze dne {Format(exact.ReadingDate)}.");
        }

        var before = ordered.LastOrDefault(r => r.ReadingDate.Date < target);
        var after = ordered.FirstOrDefault(r => r.ReadingDate.Date > target);

        if (before is not null && after is not null)
        {
            var span = (after.ReadingDate.Date - before.ReadingDate.Date).Days;
            var elapsed = (target - before.ReadingDate.Date).Days;
            var value = before.Value + (after.Value - before.Value) * elapsed / span;
            return new ReadingEstimate(Round(value), ReadingEstimateMethod.Interpolated, true,
                $"Odhad lineární interpolací po dnech mezi odečty {Format(before.ReadingDate)} ({Number(before.Value)} m³) " +
                $"a {Format(after.ReadingDate)} ({Number(after.Value)} m³), den {elapsed} z {span}.");
        }

        if (before is not null)
        {
            var days = (target - before.ReadingDate.Date).Days;
            return new ReadingEstimate(Round(before.Value), ReadingEstimateMethod.NearestBefore, true,
                $"Odhad: nejbližší předchozí odečet ze dne {Format(before.ReadingDate)} ({days} dní před datem), pozdější odečet chybí.");
        }

        if (after is not null)
        {
            var days = (after.ReadingDate.Date - target).Days;
            return new ReadingEstimate(Round(after.Value), ReadingEstimateMethod.NearestAfter, true,
                $"Odhad: nejbližší následující odečet ze dne {Format(after.ReadingDate)} ({days} dní po datu), dřívější odečet chybí.");
        }

        return new ReadingEstimate(null, ReadingEstimateMethod.None, true, "Vodoměr nemá žádný odečet, odhad nelze spočítat.");
    }

    private static decimal Round(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private static string Format(DateTime date) => date.ToString("d. M. yyyy", Czech);

    private static string Number(decimal value) => value.ToString("0.###", Czech);
}
