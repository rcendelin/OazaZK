namespace Oaza.Domain.Enums;

/// <summary>Where a cost component's amounts come from (T02).</summary>
public enum AllocationBasis
{
    /// <summary>From meter readings (water PVK); cost entries only reconcile the invoices.</summary>
    Metered,

    /// <summary>From cost entries — advances, supplier settlements, one-off costs (O3).</summary>
    CostEntries,
}
