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
            AllocationBasis = AllocationBasis.Metered, Active = false, Note = "R1",
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
