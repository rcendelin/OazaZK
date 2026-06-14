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

// ───────────────────── Saldo (per-house, per-component) ─────────────────────

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

/// <summary>
/// A house's running saldo, split into water / electricity / common base and
/// summed across all billing periods. Saldo = Charged − Paid
/// (positive = nedoplatek, negative = přeplatek).
/// </summary>
public record HouseSaldoResponse(
    string HouseId,
    string HouseName,
    SaldoComponent Water,
    SaldoComponent Electricity,
    SaldoComponent Common,
    decimal TotalCharged,
    decimal TotalPaid,
    decimal TotalSaldo,
    List<PeriodSaldoBreakdown> Periods);
