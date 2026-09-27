using Oaza.Domain.Entities;
using Oaza.Domain.Enums;

namespace Oaza.Domain.Services;

/// <param name="HouseId">The house asked.</param>
/// <param name="Expected">Amount per house of the call.</param>
/// <param name="Paid">Contributions of the house for this call.</param>
public record CallHouseStatus(string HouseId, decimal Expected, decimal Paid)
{
    public bool IsPaid => Paid >= Expected;
}

/// <summary>
/// Off-book fund math (T10): balance = contributions − expenses paid from the fund − settlements; an expense paid in
/// advance by someone is owed to them until it is settled. A call expects its amount from every house asked.
/// </summary>
public static class OffBookFundCalculator
{
    public static decimal Balance(IEnumerable<FundRecord> records)
    {
        var list = records.ToList();
        return list.Where(r => r.Kind == FundRecordKind.Contribution).Sum(r => r.Amount)
               - list.Where(r => r.Kind == FundRecordKind.Expense && r.PaidBy is null).Sum(r => r.Amount)
               - list.Where(r => r.Kind == FundRecordKind.Settlement).Sum(r => r.Amount);
    }

    /// <summary>What the fund still owes to those who paid expenses in advance.</summary>
    public static decimal Outstanding(IEnumerable<FundRecord> records)
    {
        var list = records.ToList();
        return list.Where(r => r.Kind == FundRecordKind.Expense && r.PaidBy is not null).Sum(r => r.Amount)
               - list.Where(r => r.Kind == FundRecordKind.Settlement).Sum(r => r.Amount);
    }

    public static IReadOnlyList<CallHouseStatus> CallStatus(FundRecord call, IEnumerable<FundRecord> records)
    {
        ArgumentNullException.ThrowIfNull(call);
        var contributions = records.Where(r => r.Kind == FundRecordKind.Contribution && r.CallId == call.Id).ToList();
        return call.HouseIds
            .Select(h => new CallHouseStatus(h, call.Amount, contributions.Where(c => c.HouseId == h).Sum(c => c.Amount)))
            .ToList();
    }
}
