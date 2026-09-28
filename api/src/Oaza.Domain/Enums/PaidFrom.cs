namespace Oaza.Domain.Enums;

/// <summary>How a cost was paid. Evidence only — it never changes the allocation (R6).</summary>
public enum PaidFrom
{
    Bank,

    /// <summary>Covered by a credit (overpayment) with the supplier — still a cost for the houses (R6, S2).</summary>
    SupplierCredit,

    Cash,
    Other,
}
