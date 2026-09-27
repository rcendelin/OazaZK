namespace Oaza.Domain.Enums;

/// <summary>Kind of starting value at the accounting start (T03, R3–R5).</summary>
public enum OpeningBalanceType
{
    /// <summary>Meter state in m³ (R4: nearest reading, or an interpolated estimate).</summary>
    MeterReading,

    /// <summary>
    /// The house's share in the association's fund in CZK at the last annual closing (R5).
    /// Sign as the house saldo (X1): positive = the house has a credit with the association (přeplatek).
    /// </summary>
    FundShare,

    /// <summary>
    /// A component's credit with the supplier in CZK at the start (e.g. −20 000 Kč for the waterworks electricity, O2).
    /// Negative = credit; allocated by the component's rule on that day and enters the house saldo as a negative cost.
    /// </summary>
    ComponentCredit,
}
