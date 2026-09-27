using FluentAssertions;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Services;

namespace Oaza.Domain.Tests.Services;

public class OffBookFundCalculatorTests
{
    private static FundRecord R(FundRecordKind kind, decimal amount, string? paidBy = null, string? callId = null, string? house = null) =>
        new() { Id = Guid.NewGuid().ToString(), Kind = kind, Amount = amount, PaidBy = paidBy, CallId = callId, HouseId = house };

    [Fact]
    public void BalanceAndOutstanding()
    {
        var records = new[]
        {
            R(FundRecordKind.Contribution, 5_000m),
            R(FundRecordKind.Expense, 1_000m),
            R(FundRecordKind.Expense, 2_000m, paidBy: "Jindra"),
            R(FundRecordKind.Settlement, 1_500m),
            R(FundRecordKind.Call, 999m),
        };

        OffBookFundCalculator.Balance(records).Should().Be(2_500m);
        OffBookFundCalculator.Outstanding(records).Should().Be(500m);
    }

    [Fact]
    public void CallStatusPerHouse()
    {
        var call = new FundRecord { Id = "c", Kind = FundRecordKind.Call, Amount = 1_230m, HouseIds = ["A", "B"] };
        var status = OffBookFundCalculator.CallStatus(call, [call, R(FundRecordKind.Contribution, 1_230m, callId: "c", house: "A"), R(FundRecordKind.Contribution, 500m, callId: "c", house: "B")]);

        status.Select(s => (s.HouseId, s.Paid, s.IsPaid)).Should().Equal(("A", 1_230m, true), ("B", 500m, false));
    }
}
