using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

/// <summary>Cost components (T02); PK <c>COMPONENT</c>, RK = id.</summary>
public interface ICostComponentRepository : IRepository<CostComponent>
{
    Task<IReadOnlyList<CostComponent>> GetAllComponentsAsync();
}

/// <summary>Allocation rules (T02); PK = component id, RK = rule id.</summary>
public interface IComponentAllocationRuleRepository : IRepository<ComponentAllocationRule>
{
    Task<IReadOnlyList<ComponentAllocationRule>> GetByComponentAsync(string componentId);
}

/// <summary>Participations of houses in components (T02); PK = component id, RK = participation id.</summary>
public interface IParticipationRepository : IRepository<Participation>
{
    Task<IReadOnlyList<Participation>> GetByComponentAsync(string componentId);
}
