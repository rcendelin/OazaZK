using FluentAssertions;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Helpers;

namespace Oaza.Domain.Tests.Helpers;

public class ClosedPeriodPaymentsTests
{
    private static readonly DateOnly Closed = new(2026, 9, 30);

    [Fact]
    public void AdvanceForAClosedMonthBecomesAnExtraPaymentAfterTheCut()
    {
        var payment = new AdvancePayment { HouseId = "A", Type = PaymentType.Advance, Year = 2026, Month = 9, RowKey = "2026-09", Amount = 1_500m, PaymentDate = new DateTime(2026, 9, 12) };

        ClosedPeriodPayments.BookAfterClosing(payment, Closed).Should().BeTrue();

        payment.Type.Should().Be(PaymentType.Doplatek);
        payment.RowKey.Should().StartWith("D-");
        payment.PaymentDate.Should().Be(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        (payment.Year, payment.Month).Should().Be((2026, 10));
        payment.Note.Should().Be("Záloha za 9/2026 zapsaná po mezizávěrce k 30. 9. 2026");
        payment.Amount.Should().Be(1_500m);
    }

    [Fact]
    public void LateExtraPaymentIsMovedAndKeepsItsNote()
    {
        var payment = new AdvancePayment { Type = PaymentType.Doplatek, PaymentDate = new DateTime(2026, 9, 29), Note = "z banky" };

        ClosedPeriodPayments.BookAfterClosing(payment, Closed).Should().BeTrue();

        payment.PaymentDate.Should().Be(new DateTime(2026, 10, 1));
        payment.Note.Should().Be("Platba ze dne 29. 9. 2026 zapsaná po mezizávěrce k 30. 9. 2026 — z banky");
    }

    [Fact]
    public void OpenPeriodOrNoClosingLeavesThePaymentAlone()
    {
        var payment = new AdvancePayment { Type = PaymentType.Advance, Year = 2026, Month = 10, RowKey = "2026-10", PaymentDate = new DateTime(2026, 9, 25) };

        ClosedPeriodPayments.BookAfterClosing(payment, Closed).Should().BeFalse();
        ClosedPeriodPayments.BookAfterClosing(payment, null).Should().BeFalse();
        payment.RowKey.Should().Be("2026-10");
        ClosedPeriodPayments.IsClosed(payment, Closed).Should().BeFalse();
        ClosedPeriodPayments.BookingDay(payment).Should().Be(new DateOnly(2026, 10, 1));
        ClosedPeriodPayments.IsClosed(new AdvancePayment { Type = PaymentType.Payout, PaymentDate = new DateTime(2026, 9, 30) }, Closed).Should().BeTrue();
    }
}
