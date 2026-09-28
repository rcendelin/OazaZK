namespace Oaza.Domain.Enums;

/// <summary>Kind of a record of the off-book fund (T10).</summary>
public enum FundRecordKind
{
    /// <summary>A call for contributions: amount per house, due date, the houses asked.</summary>
    Call,

    /// <summary>Money a house put into the fund (transfer or cash).</summary>
    Contribution,

    /// <summary>Money spent — paid from the fund, or paid in advance by someone (then the fund owes them).</summary>
    Expense,

    /// <summary>Reimbursement from the fund to whoever paid an expense in advance.</summary>
    Settlement,
}
