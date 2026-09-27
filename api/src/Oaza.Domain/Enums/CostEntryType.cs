namespace Oaza.Domain.Enums;

/// <summary>Kind of a component cost (T06).</summary>
public enum CostEntryType
{
    /// <summary>Advance to the supplier; spread evenly over the days of its period (R6).</summary>
    Advance,

    /// <summary>Supplier settlement (the „13th item“): positive = extra payment, negative = overpayment returned.</summary>
    Settlement,

    /// <summary>One-off cost (e.g. septic tank emptying, R9) or a supplier invoice of a metered component.</summary>
    OneOff,
}
