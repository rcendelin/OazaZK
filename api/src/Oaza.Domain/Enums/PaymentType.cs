namespace Oaza.Domain.Enums;

/// <summary>
/// Distinguishes a regular monthly advance (záloha) from an ad-hoc top-up
/// payment (doplatek). Both carry the same per-component split.
/// </summary>
public enum PaymentType
{
    /// <summary>Regular monthly advance — one per house per month (RowKey "YYYY-MM").</summary>
    Advance,

    /// <summary>Ad-hoc top-up payment — any date, repeatable (RowKey "D-...").</summary>
    Doplatek,
}
