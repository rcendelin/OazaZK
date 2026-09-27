using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Oaza.Application.DTOs;

namespace Oaza.Application.Ledger;

/// <summary>An exported file.</summary>
public record ExportFile(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Exports of the house ledger and the overview (T07) to XLSX and CSV. CSV is for Czech Excel: UTF-8 with BOM,
/// „;“ separator, decimal comma. The saldo sign follows X1 (positive = přeplatek).
/// </summary>
public static class LedgerExport
{
    public const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string CsvType = "text/csv; charset=utf-8";

    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    private static readonly Dictionary<LedgerItemKind, string> KindLabels = new()
    {
        [LedgerItemKind.Opening] = "Počáteční podíl",
        [LedgerItemKind.Payment] = "Platba",
        [LedgerItemKind.Payout] = "Výplata",
        [LedgerItemKind.Cost] = "Náklad",
        [LedgerItemKind.Credit] = "Kredit",
        [LedgerItemKind.Water] = "Voda",
        [LedgerItemKind.Loss] = "Ztráta",
    };

    public static ExportFile House(HouseLedgerResponse ledger, string format)
    {
        var header = new[] { "Datum", "Druh", "Složka", "Popis", "Částka (Kč)", "Saldo (Kč)", "Výpočet" };
        var rows = ledger.Items.Select(i => new object?[]
        {
            i.Date, KindLabels[i.Kind], i.ComponentName, i.Description, i.Amount, i.Balance, i.Detail?.Explanation,
        }).ToList();
        var name = $"saldo-{Slug(ledger.HouseName)}-{ledger.From:yyyyMMdd}-{ledger.To:yyyyMMdd}";
        var title = $"Saldo domu {ledger.HouseName} za {Day(ledger.From)} – {Day(ledger.To)} (kladné = přeplatek)";
        return Build(format, name, "Saldo", title, header, rows,
            [["Počáteční podíl", ledger.Opening], ["Platby", ledger.Payments], ["Náklady", ledger.Costs], ["Saldo", ledger.Saldo]]);
    }

    public static ExportFile Overview(LedgerOverviewResponse overview, string format)
    {
        var header = new[] { "Dům", "Počáteční podíl (Kč)" }
            .Concat(overview.Components.Select(c => $"{c.ComponentName} (Kč)"))
            .Concat(["Platby (Kč)", "Saldo (Kč)"])
            .ToArray();
        var rows = overview.Houses.Select(h => new object?[] { h.HouseName, h.Opening }
                .Concat(overview.Components.Select(c => (object?)h.Costs.GetValueOrDefault(c.ComponentId)))
                .Concat([h.Payments, h.Saldo])
                .ToArray())
            .ToList();
        rows.Add(new object?[] { "Σ domů", overview.Houses.Sum(h => h.Opening) }
            .Concat(overview.Components.Select(c => (object?)c.HousesTotal))
            .Concat([overview.Houses.Sum(h => h.Payments), overview.Houses.Sum(h => h.Saldo)])
            .ToArray());
        rows.Add(new object?[] { "Rozpočteno složkou", null }
            .Concat(overview.Components.Select(c => (object?)c.AllocatedTotal))
            .Concat([null, null])
            .ToArray());
        var name = $"saldo-prehled-{overview.From:yyyyMMdd}-{overview.To:yyyyMMdd}";
        var title = $"Saldo domů za {Day(overview.From)} – {Day(overview.To)} (kladné = přeplatek, náklady kladně)";
        return Build(format, name, "Přehled", title, header, rows, []);
    }

    private static ExportFile Build(string format, string name, string sheet, string title, string[] header, List<object?[]> rows, object?[][] summary)
    {
        switch (format.ToLowerInvariant())
        {
            case "csv":
                return new ExportFile(Csv(header, rows), CsvType, $"{name}.csv");
            case "xlsx":
                return new ExportFile(Xlsx(sheet, title, header, rows, summary), XlsxType, $"{name}.xlsx");
            default:
                throw new ArgumentException("Formát exportu musí být csv, nebo xlsx.", nameof(format));
        }
    }

    private static byte[] Csv(string[] header, IEnumerable<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', header.Select(Quote)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(';', row.Select(v => Quote(Format(v)))));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static byte[] Xlsx(string sheet, string title, string[] header, List<object?[]> rows, object?[][] summary)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(sheet);
        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        for (var c = 0; c < header.Length; c++)
        {
            ws.Cell(3, c + 1).Value = header[c];
            ws.Cell(3, c + 1).Style.Font.Bold = true;
        }
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < rows[r].Length; c++)
                SetCell(ws.Cell(4 + r, c + 1), rows[r][c]);

        var line = 5 + rows.Count;
        foreach (var item in summary)
        {
            ws.Cell(line, 1).Value = item[0]?.ToString();
            SetCell(ws.Cell(line, 2), item[1]);
            ws.Cell(line, 1).Style.Font.Bold = true;
            line++;
        }
        ws.Columns().AdjustToContents(1, 200);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void SetCell(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case decimal d:
                cell.Value = d;
                cell.Style.NumberFormat.Format = "#,##0.00";
                return;
            case DateOnly day:
                cell.Value = day.ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = "d.m.yyyy";
                return;
            default:
                cell.Value = value.ToString();
                return;
        }
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        decimal d => d.ToString("0.00", Czech),
        DateOnly day => Day(day),
        _ => value.ToString() ?? string.Empty,
    };

    private static string Quote(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private static string Day(DateOnly day) => day.ToString("d. M. yyyy", CultureInfo.InvariantCulture);

    private static string Slug(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        }
        return string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
