using FluentAssertions;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Infrastructure.Persistence;

namespace Oaza.Infrastructure.Tests;

[Collection("Azurite")]
public class AuditLogRepositoryIntegrationTests
{
    private readonly AzuriteFixture _fx;

    public AuditLogRepositoryIntegrationTests(AzuriteFixture fx) => _fx = fx;

    private static AuditLogEntry Entry(string entityId, DateTime timestamp, string entityType = "TestEntity") => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Timestamp = timestamp,
        UserId = "u-1",
        UserName = "Admin",
        EntityType = entityType,
        EntityId = entityId,
        Action = AuditActions.Update,
        OldValue = "{\"v\":1}",
        NewValue = "{\"v\":2}",
        Reason = "test",
    };

    [SkippableFact]
    public async Task Query_SpansMonthPartitions_FiltersAndSortsNewestFirst()
    {
        Skip.IfNot(_fx.Available, "Azurite emulator not available on the Table endpoint.");
        var repo = new AuditLogRepository(_fx.ServiceClient);
        var entityId = "e-" + Guid.NewGuid().ToString("N");
        // Far-past months so parallel runs / other tests don't interfere.
        var jan = new DateTime(2001, 1, 31, 23, 0, 0, DateTimeKind.Utc);
        var feb = new DateTime(2001, 2, 1, 8, 0, 0, DateTimeKind.Utc);
        var mar = new DateTime(2001, 3, 15, 12, 0, 0, DateTimeKind.Utc);

        await repo.AppendAsync(Entry(entityId, jan));
        await repo.AppendAsync(Entry(entityId, feb));
        await repo.AppendAsync(Entry(entityId, mar));
        await repo.AppendAsync(Entry("other-" + entityId, feb));

        var result = await repo.QueryAsync(
            new DateTime(2001, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2001, 2, 28, 23, 59, 59, DateTimeKind.Utc),
            entityType: "TestEntity",
            entityId: entityId);

        result.Select(e => e.Timestamp).Should().Equal(feb, jan);
        result[0].OldValue.Should().Be("{\"v\":1}");
        result[0].Reason.Should().Be("test");
        result[0].UserName.Should().Be("Admin");
    }

    [Fact]
    public void PartitionKey_IsUtcMonth()
    {
        AuditLogRepository.PartitionKeyFor(new DateTime(2026, 9, 30, 23, 30, 0, DateTimeKind.Utc)).Should().Be("2026-09");
    }
}
