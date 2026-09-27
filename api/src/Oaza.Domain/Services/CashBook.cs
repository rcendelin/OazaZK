using System.Globalization;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;

namespace Oaza.Domain.Services;

/// <summary>An entry with its effect on the cash balance and the balance after it.</summary>
public record CashBookLine(CashBookEntry Entry, decimal Effect, decimal Balance);

/// <summary>
/// Cash book rules (T09): balance = Σ deposits − Σ expenses ± corrections; the balance must never go negative on
/// any day (an expense — or a storno of a deposit — that would do so is rejected, also when back-dated).
/// </summary>
public static class CashBook
{
    /// <summary>Effect of an entry on the balance; a correction reverses its original.</summary>
    public static decimal Effect(CashBookEntry entry, IReadOnlyDictionary<string, CashBookEntry> byId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Type switch
        {
            CashBookEntryType.Deposit => entry.Amount,
            CashBookEntryType.Expense => -entry.Amount,
            _ => entry.CorrectionOf is not null && byId.TryGetValue(entry.CorrectionOf, out var original)
                ? -Effect(original, byId)
                : 0m,
        };
    }

    /// <summary>All entries in booking order (date, then time of entry) with the running balance.</summary>
    public static IReadOnlyList<CashBookLine> Lines(IEnumerable<CashBookEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = entries.OrderBy(e => e.Date).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
        var byId = list.ToDictionary(e => e.Id);
        var balance = 0m;
        var lines = new List<CashBookLine>(list.Count);
        foreach (var entry in list)
        {
            var effect = Effect(entry, byId);
            balance += effect;
            lines.Add(new CashBookLine(entry, effect, balance));
        }
        return lines;
    }

    /// <summary>The first line where the balance would be negative, or null.</summary>
    public static CashBookLine? FirstNegative(IEnumerable<CashBookEntry> entries) =>
        Lines(entries).FirstOrDefault(l => l.Balance < 0);

    /// <summary>Why the entry is not valid on its own (empty = valid).</summary>
    public static IReadOnlyList<string> Check(CashBookEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var errors = new List<string>();
        if (entry.Date == default)
            errors.Add("Datum je povinné.");
        if (entry.Amount <= 0)
            errors.Add("Částka musí být kladná.");
        else if (decimal.Round(entry.Amount, 2) != entry.Amount)
            errors.Add("Částka může mít nejvýš dvě desetinná místa.");
        if (string.IsNullOrWhiteSpace(entry.Description))
            errors.Add("Popis (co) je povinný.");
        if (entry.Type == CashBookEntryType.Expense && !entry.HasReceipt && string.IsNullOrWhiteSpace(entry.Counterparty))
            errors.Add("U výdaje bez dokladu uveďte, komu byly peníze vyplaceny.");
        if (entry.Type == CashBookEntryType.Correction && entry.CorrectionOf is null)
            errors.Add("Storno musí odkazovat na původní záznam.");
        return errors;
    }

    public static string Day(DateOnly day) => day.ToString("d. M. yyyy", CultureInfo.InvariantCulture);
}
