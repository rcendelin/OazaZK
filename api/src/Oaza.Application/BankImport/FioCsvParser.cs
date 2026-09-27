using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Oaza.Application.Exceptions;

namespace Oaza.Application.BankImport;

/// <summary>
/// Parses a Fio banka "Výpis z účtu" CSV export: a block of one-field header
/// lines (statement number + own account, period, balances, totals) followed by
/// a ';'-separated, fully quoted transaction table that starts at the row whose
/// first field is "ID operace". Columns are read by index because the export
/// contains two columns named "Poznámka".
/// </summary>
public static partial class FioCsvParser
{
    public const long MaxFileSizeBytes = 1024 * 1024;

    private const int ColId = 0;
    private const int ColDate = 1;
    private const int ColAmount = 2;
    private const int ColCurrency = 3;
    private const int ColCounterAccount = 4;
    private const int ColCounterName = 5;
    private const int ColBankCode = 6;
    private const int ColVariableSymbol = 9;
    private const int ColNote = 11;
    private const int ColMessage = 12;
    private const int ColType = 13;
    private const int ColNote2 = 16;
    private const int MinColumns = ColNote2 + 1;

    // Explicit format instead of the cs-CZ culture so parsing works in invariant-globalization hosts.
    private static readonly NumberFormatInfo CzechNumbers = new() { NumberDecimalSeparator = ",", NegativeSign = "-", PositiveSign = "+" };

    [GeneratedRegex(@"Výpis č\.\s*(\S+)\s+z účtu\s+""?(\S+?)""?$")]
    private static partial Regex StatementPattern();

    [GeneratedRegex(@"^Období:\s*(\d{2}\.\d{2}\.\d{4})\s*-\s*(\d{2}\.\d{2}\.\d{4})")]
    private static partial Regex PeriodPattern();

    [GeneratedRegex(@"^(Počáteční stav|Koncový stav) účtu k \d{2}\.\d{2}\.\d{4}:\s*([+-]?[\d\s ,.]+)\s*CZK")]
    private static partial Regex BalancePattern();

    [GeneratedRegex(@"^Suma (příjmů|výdajů):\s*([+-]?[\d\s ,.]+)\s*CZK")]
    private static partial Regex SumPattern();

    public static FioStatement Parse(byte[] content)
    {
        var text = Decode(content);
        var records = ReadRecords(text);

        var statement = new FioStatement();
        var headerIndex = records.FindIndex(r => r.Count > 0 && r[0].Trim() == "ID operace");
        if (headerIndex < 0)
        {
            throw new AppException("Soubor nevypadá jako CSV výpis z Fio banky (chybí tabulka pohybů „ID operace“).");
        }

        foreach (var record in records.Take(headerIndex))
        {
            if (record.Count == 0) continue;
            ParseHeaderLine(record[0].Trim(), statement);
        }

        if (statement.OwnAccount is null)
        {
            throw new AppException("Ve výpisu chybí číslo účtu (řádek „Výpis č. … z účtu …“).");
        }

        foreach (var (record, index) in records.Skip(headerIndex + 1).Select((r, i) => (r, i)))
        {
            if (record.All(string.IsNullOrWhiteSpace)) continue;

            var line = headerIndex + index + 2;
            if (record.Count < MinColumns)
            {
                throw new AppException($"Řádek {line} výpisu má jen {record.Count} sloupců, očekáváno alespoň {MinColumns}.");
            }

            statement.Transactions.Add(ParseTransaction(record, line));
        }

        CheckSums(statement);
        return statement;
    }

    private static void ParseHeaderLine(string line, FioStatement statement)
    {
        var m = StatementPattern().Match(line);
        if (m.Success)
        {
            statement.StatementNumber = m.Groups[1].Value;
            statement.OwnAccount = Oaza.Domain.Helpers.BankAccountNumber.TryParse(m.Groups[2].Value.Trim('"'), out var account)
                ? account
                : null;
            return;
        }

        m = PeriodPattern().Match(line);
        if (m.Success)
        {
            statement.DateFrom = ParseDate(m.Groups[1].Value);
            statement.DateTo = ParseDate(m.Groups[2].Value);
            return;
        }

        m = BalancePattern().Match(line);
        if (m.Success)
        {
            var value = ParseAmountOrNull(m.Groups[2].Value);
            if (m.Groups[1].Value.StartsWith("Počáteční")) statement.OpeningBalance = value;
            else statement.ClosingBalance = value;
            return;
        }

        m = SumPattern().Match(line);
        if (m.Success)
        {
            var value = ParseAmountOrNull(m.Groups[2].Value);
            if (m.Groups[1].Value == "příjmů") statement.TotalIncome = value;
            else statement.TotalExpense = value;
        }
    }

    private static FioTransaction ParseTransaction(IReadOnlyList<string> r, int line)
    {
        var id = r[ColId].Trim();
        if (id.Length == 0)
        {
            throw new AppException($"Řádek {line} výpisu nemá „ID operace“.");
        }

        if (!DateTime.TryParseExact(r[ColDate].Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new AppException($"Řádek {line} výpisu má neplatné datum „{r[ColDate]}“.");
        }

        var amount = ParseAmountOrNull(r[ColAmount])
            ?? throw new AppException($"Řádek {line} výpisu má neplatnou částku „{r[ColAmount]}“.");

        return new FioTransaction(
            Id: id,
            Date: DateTime.SpecifyKind(date, DateTimeKind.Utc),
            Amount: amount,
            Currency: r[ColCurrency].Trim(),
            CounterAccount: NullIfEmpty(r[ColCounterAccount]),
            CounterBankCode: NullIfEmpty(r[ColBankCode]),
            CounterName: NullIfEmpty(r[ColCounterName]),
            VariableSymbol: NormalizeSymbol(r[ColVariableSymbol]),
            Note: NullIfEmpty(r[ColNote]),
            Message: NullIfEmpty(r[ColMessage]),
            Type: NullIfEmpty(r[ColType]),
            Note2: NullIfEmpty(r[ColNote2]));
    }

    private static void CheckSums(FioStatement statement)
    {
        var income = statement.Transactions.Where(t => t.Amount > 0).Sum(t => t.Amount);
        var expense = statement.Transactions.Where(t => t.Amount < 0).Sum(t => t.Amount);

        if (statement.TotalIncome is { } totalIncome && totalIncome != income)
        {
            statement.Warnings.Add($"Součet příchozích plateb ({income:N2} Kč) nesouhlasí s „Suma příjmů“ ve výpisu ({totalIncome:N2} Kč).");
        }

        if (statement.TotalExpense is { } totalExpense && Math.Abs(totalExpense) != Math.Abs(expense))
        {
            statement.Warnings.Add($"Součet odchozích plateb ({Math.Abs(expense):N2} Kč) nesouhlasí s „Suma výdajů“ ve výpisu ({Math.Abs(totalExpense):N2} Kč).");
        }
    }

    /// <summary>UTF-8 (with or without BOM) when valid; otherwise Windows-1250, used by older exports.</summary>
    private static string Decode(byte[] content)
    {
        try
        {
            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content);
            return text.TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1250).GetString(content);
        }
    }

    /// <summary>
    /// Splits the text into records of fields. Handles quoted fields, "" escapes,
    /// ';' separators and any mix of \r\n / \r / \n line endings (the Fio export
    /// uses \r\r\n). Line breaks inside quotes are kept as part of the field.
    /// </summary>
    internal static List<List<string>> ReadRecords(string text)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
        }

        void EndRecord()
        {
            if (fieldStarted || fields.Count > 0) EndField();
            if (fields.Count > 0) records.Add(fields);
            fields = new List<string>();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    fieldStarted = true;
                    break;
                case ';':
                    EndField();
                    fieldStarted = true; // a trailing ';' still yields an (empty) last field
                    break;
                case '\r':
                case '\n':
                    EndRecord();
                    break;
                default:
                    field.Append(c);
                    fieldStarted = true;
                    break;
            }
        }

        EndRecord();
        return records;
    }

    private static DateTime ParseDate(string value) =>
        DateTime.SpecifyKind(DateTime.ParseExact(value, "dd.MM.yyyy", CultureInfo.InvariantCulture), DateTimeKind.Utc);

    /// <summary>Czech number format: decimal comma, optional sign, spaces (incl. NBSP) as thousand separators.</summary>
    private static decimal? ParseAmountOrNull(string value)
    {
        var cleaned = value.Replace(" ", string.Empty).Replace(" ", string.Empty).Trim();
        return decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CzechNumbers, out var result)
            ? result
            : null;
    }

    private static string? NullIfEmpty(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>Symbols made only of zeros ("0", "0000") mean "none".</summary>
    private static string? NormalizeSymbol(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 || trimmed.All(ch => ch == '0') ? null : trimmed;
    }
}

public class FioStatement
{
    public Oaza.Domain.Helpers.BankAccountNumber? OwnAccount { get; set; }
    public string? StatementNumber { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public decimal? OpeningBalance { get; set; }
    public decimal? ClosingBalance { get; set; }
    public decimal? TotalIncome { get; set; }
    public decimal? TotalExpense { get; set; }
    public List<FioTransaction> Transactions { get; } = new();
    public List<string> Warnings { get; } = new();
}

public record FioTransaction(
    string Id,
    DateTime Date,
    decimal Amount,
    string Currency,
    string? CounterAccount,
    string? CounterBankCode,
    string? CounterName,
    string? VariableSymbol,
    string? Note,
    string? Message,
    string? Type,
    string? Note2);
