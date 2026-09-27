using FluentAssertions;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Infrastructure.Persistence;

namespace Oaza.Infrastructure.Tests;

[Collection("Azurite")]
public class MeterReadingRepositoryIntegrationTests
{
    private readonly AzuriteFixture _fx;

    public MeterReadingRepositoryIntegrationTests(AzuriteFixture fx) => _fx = fx;

    private static MeterReading Reading(string meterId, DateTime date, decimal value) => new()
    {
        MeterId = meterId,
        ReadingDate = date,
        Value = value,
        Source = ReadingSource.Manual,
        ImportedAt = DateTime.UtcNow,
        ImportedBy = "integration-test",
    };

    [SkippableFact]
    public async Task GetByMeterId_ReturnsReadingsNewestFirst_ViaInvertedTimestampRowKey()
    {
        Skip.IfNot(_fx.Available, "Azurite emulator not available on the Table endpoint.");
        var repo = new MeterReadingRepository(_fx.ServiceClient);
        var meterId = "meter-" + Guid.NewGuid().ToString("N");

        var jan = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var mar = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var jun = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        // Insert out of order to prove the RowKey (inverted timestamp), not insertion
        // order, drives the result ordering.
        await repo.UpsertAsync(Reading(meterId, mar, 130m));
        await repo.UpsertAsync(Reading(meterId, jun, 160m));
        await repo.UpsertAsync(Reading(meterId, jan, 100m));

        var all = await repo.GetByMeterIdAsync(meterId);
        all.Select(r => r.ReadingDate).Should().Equal(jun, mar, jan);

        var latest2 = await repo.GetLatestByMeterIdAsync(meterId, 2);
        latest2.Select(r => r.ReadingDate).Should().Equal(jun, mar);
        latest2.Select(r => r.Value).Should().Equal(160m, 130m);
    }

    [SkippableFact]
    public async Task EstimateFlag_RoundTrips_AndDefaultsToFalse()
    {
        Skip.IfNot(_fx.Available, "Azurite emulator not available on the Table endpoint.");
        var repo = new MeterReadingRepository(_fx.ServiceClient);
        var meterId = "meter-" + Guid.NewGuid().ToString("N");

        var estimate = Reading(meterId, new DateTime(2025, 1, 16, 0, 0, 0, DateTimeKind.Utc), 260.855m);
        estimate.IsEstimate = true;
        estimate.EstimateNote = "Odhad lineární interpolací";
        await repo.UpsertAsync(estimate);
        await repo.UpsertAsync(Reading(meterId, new DateTime(2025, 2, 16, 0, 0, 0, DateTimeKind.Utc), 270m));

        var stored = await repo.GetByMeterIdAsync(meterId);

        var feb = stored.Single(r => r.ReadingDate.Month == 2);
        feb.IsEstimate.Should().BeFalse();
        feb.EstimateNote.Should().BeNull();
        var jan = stored.Single(r => r.ReadingDate.Month == 1);
        jan.IsEstimate.Should().BeTrue();
        jan.EstimateNote.Should().Be("Odhad lineární interpolací");
        jan.Value.Should().Be(260.855m);
    }
}
