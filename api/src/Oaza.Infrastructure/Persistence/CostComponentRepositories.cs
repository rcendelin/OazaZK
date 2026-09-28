using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class CostComponentRepository : TableStorageRepository<CostComponent>, ICostComponentRepository
{
    public CostComponentRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.CostComponents)
    {
    }

    protected override TableEntity ToTableEntity(CostComponent entity) => TableEntityMapper.ToTableEntity(entity);

    protected override CostComponent FromTableEntity(TableEntity tableEntity) => TableEntityMapper.ToCostComponent(tableEntity);

    public Task<IReadOnlyList<CostComponent>> GetAllComponentsAsync() => GetByPartitionKeyAsync(PartitionKeys.CostComponent);
}

public class ComponentAllocationRuleRepository : TableStorageRepository<ComponentAllocationRule>, IComponentAllocationRuleRepository
{
    public ComponentAllocationRuleRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.ComponentAllocationRules)
    {
    }

    protected override TableEntity ToTableEntity(ComponentAllocationRule entity) => TableEntityMapper.ToTableEntity(entity);

    protected override ComponentAllocationRule FromTableEntity(TableEntity tableEntity) => TableEntityMapper.ToComponentAllocationRule(tableEntity);

    public Task<IReadOnlyList<ComponentAllocationRule>> GetByComponentAsync(string componentId) => GetByPartitionKeyAsync(componentId);
}

public class ParticipationRepository : TableStorageRepository<Participation>, IParticipationRepository
{
    public ParticipationRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.Participations)
    {
    }

    protected override TableEntity ToTableEntity(Participation entity) => TableEntityMapper.ToTableEntity(entity);

    protected override Participation FromTableEntity(TableEntity tableEntity) => TableEntityMapper.ToParticipation(tableEntity);

    public Task<IReadOnlyList<Participation>> GetByComponentAsync(string componentId) => GetByPartitionKeyAsync(componentId);
}
