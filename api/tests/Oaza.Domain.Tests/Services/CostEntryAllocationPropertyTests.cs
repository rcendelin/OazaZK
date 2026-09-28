using FsCheck;
using FsCheck.Xunit;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Services;
using Oaza.Domain.Time;

namespace Oaza.Domain.Tests.Services;

/// <summary>T07 property: Σ of all house shares = the allocated amount, for random amounts, periods and participation.</summary>
public class CostEntryAllocationPropertyTests
{
    private static readonly DateOnly Base = new(2024, 1, 1);

    [Property(MaxTest = 300)]
    public bool SharesAlwaysSumToTheAmount(int cents, NonNegativeInt start, NonNegativeInt length, PositiveInt houses, NonNegativeInt[] joins)
    {
        var amount = cents % 100_000_000 * 0.01m;
        var period = new DateRange(Base.AddDays(start.Get % 400), Base.AddDays(start.Get % 400 + length.Get % 200));
        var count = houses.Get % 8 + 1;
        // Every house participates from the base date or from a random later day; the first always from the base.
        var participations = Enumerable.Range(0, count).Select(i => new Participation
        {
            HouseId = $"H{i}",
            ValidFrom = i == 0 || joins.Length == 0 ? Base : Base.AddDays(joins[i % joins.Length].Get % 600),
        }).ToList();
        var rule = new ComponentAllocationRule { ValidFrom = Base, Method = AllocationMethod.Equal };

        var shares = CostEntryAllocation.Allocate(amount, period, [rule], participations, CostEntryAllocation.MonthStarts(period));

        return shares.Sum(s => s.Amount) == amount && shares.All(s => decimal.Round(s.Amount, 2) == s.Amount);
    }
}
