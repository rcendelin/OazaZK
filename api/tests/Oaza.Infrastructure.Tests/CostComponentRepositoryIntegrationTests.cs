using FluentAssertions;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Infrastructure.Persistence;

namespace Oaza.Infrastructure.Tests;

[Collection("Azurite")]
public class CostComponentRepositoryIntegrationTests
{
    private readonly AzuriteFixture _fx;

    public CostComponentRepositoryIntegrationTests(AzuriteFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task ComponentRulesAndParticipations_RoundTripThroughTableStorage()
    {
        Skip.IfNot(_fx.Available, "Azurite emulator not available on the Table endpoint.");
        var components = new CostComponentRepository(_fx.ServiceClient);
        var rules = new ComponentAllocationRuleRepository(_fx.ServiceClient);
        var participations = new ParticipationRepository(_fx.ServiceClient);
        var id = Guid.NewGuid().ToString();

        await components.UpsertAsync(new CostComponent
        {
            Id = id, Name = "Elektřina – vodárna", Code = "T_" + id[..8].ToUpperInvariant(), StartDate = new DateOnly(2023, 11, 1),
            AllocationBasis = AllocationBasis.CostEntries,
        });
        await rules.UpsertAsync(new ComponentAllocationRule { Id = "r1", ComponentId = id, ValidFrom = new DateOnly(2023, 11, 1), Method = AllocationMethod.Equal });
        foreach (var house in new[] { "A", "B", "C", "D" })
            await participations.UpsertAsync(new Participation { Id = house, ComponentId = id, HouseId = house, ValidFrom = new DateOnly(2023, 11, 1) });
        await participations.UpsertAsync(new Participation { Id = "x", ComponentId = "other-" + id, HouseId = "E", ValidFrom = new DateOnly(2024, 1, 1) });

        (await components.GetAsync(PartitionKeys.CostComponent, id))!.StartDate.Should().Be(new DateOnly(2023, 11, 1));
        (await components.GetAllComponentsAsync()).Should().Contain(c => c.Id == id);
        (await rules.GetByComponentAsync(id)).Should().ContainSingle().Which.ValidTo.Should().BeNull();
        (await participations.GetByComponentAsync(id)).Select(p => p.HouseId).Should().BeEquivalentTo("A", "B", "C", "D");

        await participations.DeleteAsync(id, "D");
        (await participations.GetByComponentAsync(id)).Should().HaveCount(3);
    }
}
