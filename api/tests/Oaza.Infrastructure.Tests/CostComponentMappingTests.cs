using FluentAssertions;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Infrastructure.Persistence;

namespace Oaza.Infrastructure.Tests;

/// <summary>Round-trip of the T02 entities through Table Storage entities (no Azurite needed).</summary>
public class CostComponentMappingTests
{
    [Fact]
    public void ComponentRoundTrips_WithDayAsIsoString()
    {
        var component = new CostComponent
        {
            Id = "c1", Name = "Voda PVK", Code = "VODA_PVK", StartDate = new DateOnly(2023, 11, 1),
            AllocationBasis = AllocationBasis.Metered, WaterRole = WaterRole.Consumption, Active = false, Note = "R1",
        };

        var entity = TableEntityMapper.ToTableEntity(component);

        entity.GetString("StartDate").Should().Be("2023-11-01");
        TableEntityMapper.ToCostComponent(entity).Should().BeEquivalentTo(component);
    }

    [Fact]
    public void RuleAndParticipationRoundTrip_IncludingOpenEndAndWeight()
    {
        var rule = new ComponentAllocationRule
        {
            Id = "r1", ComponentId = "c1", ValidFrom = new DateOnly(2026, 10, 1), ValidTo = null,
            Method = AllocationMethod.Ratio, RatioSource = "VODA_PVK", Reason = "hlasování",
        };
        var participation = new Participation
        {
            Id = "p1", ComponentId = "c1", HouseId = "h1", ValidFrom = new DateOnly(2023, 11, 1),
            ValidTo = new DateOnly(2026, 3, 14), Weight = 33.33m,
        };

        var ruleEntity = TableEntityMapper.ToTableEntity(rule);
        var participationEntity = TableEntityMapper.ToTableEntity(participation);

        ruleEntity.PartitionKey.Should().Be("c1");
        participationEntity.GetString("ValidTo").Should().Be("2026-03-14");
        TableEntityMapper.ToComponentAllocationRule(ruleEntity).Should().BeEquivalentTo(rule);
        TableEntityMapper.ToParticipation(participationEntity).Should().BeEquivalentTo(participation);
    }

    [Fact]
    public void MissingOrInvalidDayReadsAsNull()
    {
        var entity = new Azure.Data.Tables.TableEntity("c1", "p1") { { "ValidFrom", "1. 11. 2023" } };

        TableEntityMapper.GetIsoDay(entity, "ValidFrom").Should().BeNull();
        TableEntityMapper.GetIsoDay(entity, "ValidTo").Should().BeNull();
        TableEntityMapper.ToIsoDay(null).Should().BeNull();
    }
}

public class OpeningBalanceMappingTests
{
    [Fact]
    public void OwnershipPeriodUsesHouseAndStartDayAsKeys()
    {
        var period = new OwnershipPeriod
        {
            Id = "h1|2023-11-01", HouseId = "h1", OwnerName = "Novákovi", Contact = "novak@example.cz",
            ValidFrom = new DateOnly(2023, 11, 1), ValidTo = new DateOnly(2026, 3, 14),
        };

        var entity = TableEntityMapper.ToTableEntity(period);

        entity.PartitionKey.Should().Be("h1");
        entity.RowKey.Should().Be("2023-11-01");
        TableEntityMapper.ToOwnershipPeriod(entity).Should().BeEquivalentTo(period);
    }

    [Fact]
    public void OpeningBalanceRoundTrips()
    {
        var balance = new OpeningBalance
        {
            Key = "MeterReading|m1|h1|2023-11-01", Type = OpeningBalanceType.MeterReading, HouseId = "h1", MeterId = "m1",
            OwnershipPeriodId = "h1|2023-11-01", Date = new DateOnly(2023, 11, 1), Value = 260.855m, IsEstimate = true,
            Source = "interpolace", Note = "S8",
        };

        var entity = TableEntityMapper.ToTableEntity(balance);

        entity.PartitionKey.Should().Be("OPENING");
        entity.GetString("Value").Should().Be("260.855");
        TableEntityMapper.ToOpeningBalance(entity).Should().BeEquivalentTo(balance);
    }
}

public class CostEntryMappingTests
{
    [Fact]
    public void CostEntryRoundTrips()
    {
        var entry = new CostEntry
        {
            Id = "e1", ComponentId = "pvk", Type = CostEntryType.OneOff, PeriodFrom = new DateOnly(2024, 1, 1), PeriodTo = new DateOnly(2024, 1, 31),
            Amount = -1_234.56m, QuantityM3 = 50.5m, Supplier = "PVK", DocumentId = "d1", PaidFrom = PaidFrom.SupplierCredit, Note = "n",
        };

        var entity = TableEntityMapper.ToTableEntity(entry);

        entity.PartitionKey.Should().Be("pvk");
        TableEntityMapper.ToCostEntry(entity).Should().BeEquivalentTo(entry);
    }
}

public class InterimClosingMappingTests
{
    [Fact]
    public void InterimClosingRoundTrips_AndCostEntryKeepsPosting()
    {
        var closing = new InterimClosing
        {
            Id = "2026-12-31|All|-", Date = new DateOnly(2026, 12, 31), Scope = ClosingScope.All, Reason = "roční závěrka",
            CreatedBy = "u", CreatedByName = "Rosťa", CreatedAt = new DateTime(2027, 1, 5, 8, 0, 0, DateTimeKind.Utc), SnapshotJson = "[{\"saldo\":1}]",
        };
        TableEntityMapper.ToInterimClosing(TableEntityMapper.ToTableEntity(closing)).Should().BeEquivalentTo(closing);

        var entry = new CostEntry
        {
            Id = "e", ComponentId = "c", PeriodFrom = new DateOnly(2026, 7, 1), PeriodTo = new DateOnly(2026, 12, 31), Amount = 1m,
            PostingDate = new DateOnly(2026, 10, 1), CorrectionOf = "e0",
        };
        var back = TableEntityMapper.ToCostEntry(TableEntityMapper.ToTableEntity(entry));
        back.PostingDate.Should().Be(new DateOnly(2026, 10, 1));
        back.CorrectionOf.Should().Be("e0");
        back.LockDate.Should().Be(new DateOnly(2026, 10, 1));
    }
}
