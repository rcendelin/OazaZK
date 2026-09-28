using System.Globalization;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;

namespace Oaza.Domain.Helpers;

/// <summary>
/// Payments against interim closings (T08, decided 27. 9. 2026): a payment dated in a closed period cannot be changed
/// or deleted, and a payment entered late with such a date is booked on the first open day instead (a monthly advance
/// for a closed month becomes an extra payment), with the original date in its note.
/// </summary>
public static class ClosedPeriodPayments
{
    /// <summary>The day that decides whether the payment is closed: an advance's month start, else its payment day.</summary>
    public static DateOnly BookingDay(AdvancePayment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return payment.Type == PaymentType.Advance
            ? new DateOnly(payment.Year, payment.Month, 1)
            : DateOnly.FromDateTime(payment.PaymentDate);
    }

    public static bool IsClosed(AdvancePayment payment, DateOnly? lastClosed) =>
        lastClosed is { } closed && BookingDay(payment) <= closed;

    /// <summary>Moves a new payment dated in a closed period to the first open day; returns true when it did.</summary>
    public static bool BookAfterClosing(AdvancePayment payment, DateOnly? lastClosed)
    {
        if (!IsClosed(payment, lastClosed))
            return false;

        var closed = lastClosed!.Value;
        var posting = closed.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var original = payment.Type == PaymentType.Advance
            ? $"Záloha za {payment.Month}/{payment.Year}"
            : $"Platba ze dne {Day(DateOnly.FromDateTime(payment.PaymentDate))}";
        if (payment.Type == PaymentType.Advance)
        {
            payment.Type = PaymentType.Doplatek;
            payment.RowKey = PaymentRowKeys.Doplatek(posting);
        }
        payment.PaymentDate = posting;
        payment.Year = posting.Year;
        payment.Month = posting.Month;
        var note = $"{original} zapsaná po mezizávěrce k {Day(closed)}";
        payment.Note = string.IsNullOrWhiteSpace(payment.Note) ? note : $"{note} — {payment.Note}";
        return true;
    }

    private static string Day(DateOnly day) => day.ToString("d. M. yyyy", CultureInfo.InvariantCulture);
}
