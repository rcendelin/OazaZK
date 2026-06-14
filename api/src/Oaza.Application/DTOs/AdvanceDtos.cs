namespace Oaza.Application.DTOs;

/// <summary>Records a regular monthly advance (one per house per month).</summary>
public class CreateAdvanceRequest
{
    public string HouseId { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal WaterAmount { get; set; }
    public decimal ElectricityAmount { get; set; }
    public decimal CommonAmount { get; set; }
    public DateTime PaymentDate { get; set; }
}

/// <summary>Updates the component amounts / date of an existing monthly advance.</summary>
public class UpdateAdvanceRequest
{
    public decimal WaterAmount { get; set; }
    public decimal ElectricityAmount { get; set; }
    public decimal CommonAmount { get; set; }
    public DateTime PaymentDate { get; set; }
}

/// <summary>Records an ad-hoc top-up (doplatek) for a house — any date, repeatable.</summary>
public class CreateDoplatekRequest
{
    public string HouseId { get; set; } = string.Empty;
    public decimal WaterAmount { get; set; }
    public decimal ElectricityAmount { get; set; }
    public decimal CommonAmount { get; set; }
    public DateTime PaymentDate { get; set; }
    public string? Note { get; set; }
}

public class AdvanceResponse
{
    public string HouseId { get; set; } = string.Empty;
    public string? HouseName { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public decimal WaterAmount { get; set; }
    public decimal ElectricityAmount { get; set; }
    public decimal CommonAmount { get; set; }
    public DateTime PaymentDate { get; set; }
    public string Type { get; set; } = "Advance";
    public string? Note { get; set; }
    public string RowKey { get; set; } = string.Empty;
}

/// <summary>Records a refund of a household's overpayment (money paid back). Net-level.</summary>
public class CreatePayoutRequest
{
    public string HouseId { get; set; } = string.Empty;
    public decimal Amount { get; set; }     // > 0, money returned to the household
    public DateTime PaymentDate { get; set; }
    public string? Note { get; set; }
}

/// <summary>Seeds a household's opening account balance (one-time, for system start). Net-level.</summary>
public class CreateOpeningBalanceRequest
{
    public string HouseId { get; set; } = string.Empty;
    public decimal Amount { get; set; }     // > 0 magnitude
    public bool IsOverpayment { get; set; } // true = přeplatek (credit), false = nedoplatek (debt)
    public DateTime PaymentDate { get; set; }
    public string? Note { get; set; }
}

// ───────────────────── Saldo (per-house) ─────────────────────

/// <summary>One component's account: how much was charged, paid, and the net.</summary>
public record SaldoComponent(decimal Charged, decimal Paid, decimal Saldo);

/// <summary>Per-period contribution to a house's saldo (for the breakdown view).</summary>
public record PeriodSaldoBreakdown(
    string PeriodId,
    string PeriodName,
    bool Closed,
    SaldoComponent Water,
    SaldoComponent Electricity,
    SaldoComponent Common);

/// <summary>A net-level adjustment (payout or opening balance) shown in the saldo detail.</summary>
public record SaldoAdjustment(
    string Type,        // "Payout" | "OpeningBalance"
    decimal Amount,     // signed effect on saldo (positive = increases nedoplatek)
    DateTime Date,
    string? Note,
    string RowKey);

/// <summary>
/// A house's running saldo. The headline is the single net <see cref="TotalSaldo"/>
/// (positive = nedoplatek, negative = přeplatek); money is fungible across
/// components. Water/Electricity/Common are an analytical breakdown of where the
/// charges arose. Net-level adjustments (payouts, opening balance) move the total
/// but are not attributed to a component.
/// </summary>
public record HouseSaldoResponse(
    string HouseId,
    string HouseName,
    SaldoComponent Water,
    SaldoComponent Electricity,
    SaldoComponent Common,
    decimal ComponentSaldo,        // water + electricity + common saldo
    decimal NetAdjustments,        // Σ payouts + opening balance (signed)
    decimal TotalSaldo,            // ComponentSaldo + NetAdjustments — the net balance
    decimal PrescribedMonthly,     // expected monthly payment (override sum), 0 if unset
    decimal? MonthsCovered,        // |overpayment| / prescribed, null if not in credit / no prescribed
    bool Dissolving,               // household is dissolving its overpayment
    List<PeriodSaldoBreakdown> Periods,
    List<SaldoAdjustment> Adjustments);
