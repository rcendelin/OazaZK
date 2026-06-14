namespace Oaza.Domain.Enums;

/// <summary>
/// Kind of household account transaction.
/// Advance and Doplatek are component-split money IN and feed the water
/// settlement; Payout and OpeningBalance are net-level adjustments that move the
/// running saldo only and are NEVER folded into a period's settlement advances.
/// </summary>
public enum PaymentType
{
    /// <summary>Regular monthly advance — one per house per month (RowKey "YYYY-MM"). Component-split, settlement sees it.</summary>
    Advance,

    /// <summary>Ad-hoc top-up payment — any date, repeatable (RowKey "D-..."). Component-split, settlement sees it.</summary>
    Doplatek,

    /// <summary>Refund of an overpayment paid back to the household (money OUT). Net-level; increases saldo toward zero. Amount &gt; 0.</summary>
    Payout,

    /// <summary>Opening account balance to seed the system. Net-level; signed Amount (positive = nedoplatek/debt, negative = přeplatek/credit).</summary>
    OpeningBalance,
}
