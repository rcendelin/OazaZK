namespace Oaza.Domain.Enums;

/// <summary>What an interim closing fixes (T08).</summary>
public enum ClosingScope
{
    /// <summary>All houses (e.g. the annual closing at 31. 12.).</summary>
    All,

    /// <summary>One house (e.g. a sale — the old owner's closing saldo, T03).</summary>
    House,
}
