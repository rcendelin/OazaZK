using FluentAssertions;
using Oaza.Application.Interfaces;
using Oaza.Application.Tests.TestSupport;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;

namespace Oaza.Application.Tests.UseCases;

/// <summary>Live E2E finding #15: a single house's closing (a sale) must not lock other houses' meters or the cash book.</summary>
public class ClosingBoundaryScopeTests
{
    private readonly MemoryInterimClosings _closings = new();

    public ClosingBoundaryScopeTests()
    {
        _closings.UpsertAsync(new InterimClosing { Id = "all", Date = new DateOnly(2025, 12, 31), Scope = ClosingScope.All });
        _closings.UpsertAsync(new InterimClosing { Id = "sale", Date = new DateOnly(2026, 3, 14), Scope = ClosingScope.House, HouseId = "B" });
    }

    [Fact]
    public async Task AllHousesClosing_IgnoresASingleHouseClosing()
    {
        var boundary = new InterimClosingBoundary(_closings);

        (await boundary.GetLastAllHousesClosedDayAsync()).Should().Be(new DateOnly(2025, 12, 31));
        (await boundary.GetLastClosedDayAsync()).Should().Be(new DateOnly(2026, 3, 14));
        (await boundary.GetLastClosedDayAsync("A")).Should().Be(new DateOnly(2025, 12, 31));
        (await ((IClosingBoundary)new NoClosingBoundary()).GetLastAllHousesClosedDayAsync()).Should().BeNull();
    }

    [Fact]
    public async Task MeterClosingDays_HouseMeterByItsHouse_MainMeterByAnyClosing()
    {
        var days = new MeterClosingDays(new InterimClosingBoundary(_closings));

        (await days.ForAsync(new WaterMeter { Id = "mA", Type = MeterType.Individual, HouseId = "A" })).Should().Be(new DateOnly(2025, 12, 31));
        (await days.ForAsync(new WaterMeter { Id = "mB", Type = MeterType.Individual, HouseId = "B" })).Should().Be(new DateOnly(2026, 3, 14));
        (await days.ForAsync(new WaterMeter { Id = "main", Type = MeterType.Main })).Should().Be(new DateOnly(2026, 3, 14));
        (await days.ForAsync(new WaterMeter { Id = "mA2", Type = MeterType.Individual, HouseId = "A" })).Should().Be(new DateOnly(2025, 12, 31)); // cached
    }
}
