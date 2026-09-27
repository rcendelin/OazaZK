using Oaza.Domain.Time;

namespace Oaza.Domain.Services;

/// <summary>Periodicity of a recurring supplier advance (T06).</summary>
public enum AdvancePeriodicity
{
    Monthly = 1,
    Quarterly = 3,
    HalfYearly = 6,
    Yearly = 12,
}

/// <summary>
/// Periods of a recurring advance (the „Opakovaná záloha“ form, T06): consecutive periods of whole months
/// from <c>from</c>, each one advance of the same amount. The last period ends on <c>to</c> at the latest.
/// </summary>
public static class RecurringAdvance
{
    /// <summary>At most 10 years of monthly advances in one go.</summary>
    public const int MaxPeriods = 120;

    public static IReadOnlyList<DateRange> Periods(DateOnly from, DateOnly to, AdvancePeriodicity periodicity)
    {
        if (from > to)
            throw new ArgumentException("Začátek musí být nejpozději v den konce.", nameof(from));
        if (!Enum.IsDefined(periodicity))
            throw new ArgumentOutOfRangeException(nameof(periodicity));

        var months = (int)periodicity;
        var periods = new List<DateRange>();
        // Each period start is counted from `from` (not from the previous start), so a start on the 31st
        // does not drift to the 28th after February.
        for (var k = 0; from.AddMonths(k * months) <= to; k++)
        {
            if (k >= MaxPeriods)
                throw new ArgumentException($"Najednou lze vytvořit nejvýš {MaxPeriods} záloh.", nameof(to));
            var start = from.AddMonths(k * months);
            var end = from.AddMonths((k + 1) * months).AddDays(-1);
            periods.Add(new DateRange(start, end < to ? end : to));
        }
        return periods;
    }
}
