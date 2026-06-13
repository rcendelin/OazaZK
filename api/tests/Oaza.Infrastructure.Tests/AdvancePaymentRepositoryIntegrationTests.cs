using FluentAssertions;
using Oaza.Domain.Entities;
using Oaza.Infrastructure.Persistence;

namespace Oaza.Infrastructure.Tests;

[Collection("Azurite")]
public class AdvancePaymentRepositoryIntegrationTests
{
    private readonly AzuriteFixture _fx;

    public AdvancePaymentRepositoryIntegrationTests(AzuriteFixture fx) => _fx = fx;

    private static AdvancePayment Payment(string houseId, int year, int month, decimal amount) => new()
    {
        HouseId = houseId,
        Year = year,
        Month = month,
        Amount = amount,
        PaymentDate = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [SkippableFact]
    public async Task GetByHouseAndPeriod_FiltersByMonthWithinPeriodDates()
    {
        Skip.IfNot(_fx.Available, "Azurite emulator not available on the Table endpoint.");
        var repo = new AdvancePaymentRepository(_fx.ServiceClient);
        var houseId = "house-" + Guid.NewGuid().ToString("N");

        await repo.UpsertAsync(Payment(houseId, 2025, 1, 1000m)); // before period
        await repo.UpsertAsync(Payment(houseId, 2025, 3, 1100m)); // inside period
        await repo.UpsertAsync(Payment(houseId, 2025, 6, 1200m)); // after period

        var from = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 5, 31, 0, 0, 0, DateTimeKind.Utc);
        var inRange = await repo.GetByHouseAndPeriodAsync(houseId, from, to);

        inRange.Should().HaveCount(1);
        inRange[0].Month.Should().Be(3);
        inRange[0].Amount.Should().Be(1100m);

        // Sanity: the full partition still holds all three rows.
        (await repo.GetByHouseIdAsync(houseId)).Should().HaveCount(3);
    }
}
