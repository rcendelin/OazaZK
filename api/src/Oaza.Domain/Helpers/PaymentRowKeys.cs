namespace Oaza.Domain.Helpers;

/// <summary>RowKeys of <see cref="Entities.AdvancePayment"/> records, shared by all code that creates payments.</summary>
public static class PaymentRowKeys
{
    /// <summary>Regular monthly advance — one per house per month.</summary>
    public static string Advance(int year, int month) => $"{year:D4}-{month:D2}";

    /// <summary>Ad-hoc top-up: unique, newest-first so multiple doplatky per month don't collide.</summary>
    public static string Doplatek(DateTime paymentDate) =>
        $"D-{InvertedTimestamp.FromDateTime(paymentDate)}-{Guid.NewGuid():N}"[..40];
}
