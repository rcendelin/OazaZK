using FluentAssertions;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Services;

namespace Oaza.Domain.Tests.Services;

public class CashBookTests
{
    private static CashBookEntry E(string id, int day, CashBookEntryType type, decimal amount, string? correctionOf = null) => new()
    {
        Id = id, Date = new DateOnly(2026, 9, day), Type = type, Amount = amount, CorrectionOf = correctionOf, Description = "x", Counterparty = "y",
    };

    [Fact]
    public void BalanceRunsInDateOrder_AndAStornoReversesItsOriginal()
    {
        var lines = CashBook.Lines([
            E("s", 5, CashBookEntryType.Correction, 2_000m, "e"),
            E("d", 1, CashBookEntryType.Deposit, 5_000m),
            E("e", 2, CashBookEntryType.Expense, 2_000m),
        ]);

        lines.Select(l => (l.Entry.Id, l.Effect, l.Balance)).Should().Equal(("d", 5_000m, 5_000m), ("e", -2_000m, 3_000m), ("s", 2_000m, 5_000m));
        CashBook.FirstNegative([E("d", 1, CashBookEntryType.Deposit, 100m), E("e", 2, CashBookEntryType.Expense, 101m)])!.Balance.Should().Be(-1m);
        CashBook.Effect(E("x", 1, CashBookEntryType.Correction, 5m, "missing"), new Dictionary<string, CashBookEntry>()).Should().Be(0m);
    }

    [Fact]
    public void ChecksRequiredFields()
    {
        CashBook.Check(new CashBookEntry { Type = CashBookEntryType.Expense, Amount = 1.001m })
            .Should().BeEquivalentTo(
                "Datum je povinné.", "Částka může mít nejvýš dvě desetinná místa.", "Popis (co) je povinný.",
                "U výdaje bez dokladu uveďte, komu byly peníze vyplaceny.");
        CashBook.Check(new CashBookEntry { Type = CashBookEntryType.Correction, Date = new DateOnly(2026, 1, 1), Amount = -1, Description = "x" })
            .Should().BeEquivalentTo("Částka musí být kladná.", "Storno musí odkazovat na původní záznam.");
    }
}
