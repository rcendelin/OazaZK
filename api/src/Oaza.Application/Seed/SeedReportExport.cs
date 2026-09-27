using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Oaza.Application.Ledger;

namespace Oaza.Application.Seed;

/// <summary>The seed import report (T13) as Markdown or XLSX — for the review before the live import.</summary>
public static class SeedReportExport
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    public static ExportFile Build(SeedImportReport report, string format)
    {
        ArgumentNullException.ThrowIfNull(report);
        var name = $"import-pocatecnich-dat-{report.Today:yyyyMMdd}";
        return format.ToLowerInvariant() switch
        {
            "md" => new ExportFile(Encoding.UTF8.GetBytes(Markdown(report)), "text/markdown; charset=utf-8", $"{name}.md"),
            "xlsx" => new ExportFile(Xlsx(report), LedgerExport.XlsxType, $"{name}.xlsx"),
            _ => throw new ArgumentException("Formát reportu musí být md, nebo xlsx.", nameof(format)),
        };
    }

    private static string Status(SeedImportReport r) =>
        r.Applied ? "Zapsáno." : r.CanApply ? "Zkouška prošla — lze zapsat." : r.Issues.Count > 0 ? "Nelze zapsat — opravte chyby a konflikty." : "Není co zapsat — vše už existuje.";

    private static string Money(decimal v) => v.ToString("#,##0.00", Czech).Replace('\u00A0', ' ');

    private static string Day(DateOnly d) => d.ToString("d. M. yyyy", CultureInfo.InvariantCulture);

    private static string Cell(string? v) => (v ?? string.Empty).Replace("|", "\\|").Replace("\n", " ");

    private static string Markdown(SeedImportReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Import počátečních dat — report");
        sb.AppendLine();
        sb.AppendLine($"Stav k {Day(r.Today)}: **{Status(r)}** Salda za {Day(r.From)} – {Day(r.Today)}, znaménko: kladné = přeplatek, záporné = nedoplatek.");
        sb.AppendLine();
        sb.AppendLine("## Soubory");
        sb.AppendLine();
        sb.AppendLine("| Soubor | Řádků | Nové | Beze změny | Konflikty | Chyby |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var f in r.Files)
            sb.AppendLine(f.Uploaded ? $"| {f.File} | {f.Rows} | {f.Created} | {f.Unchanged} | {f.Conflicts} | {f.Errors} |" : $"| {f.File} | nenahráno | | | | |");
        sb.AppendLine();
        sb.AppendLine("## Problémy");
        sb.AppendLine();
        if (r.Issues.Count == 0)
        {
            sb.AppendLine("Žádné.");
        }
        else
        {
            sb.AppendLine("| Soubor | Řádek | Druh | Popis |");
            sb.AppendLine("|---|---:|---|---|");
            foreach (var i in r.Issues)
                sb.AppendLine($"| {Cell(i.File)} | {i.Line?.ToString(CultureInfo.InvariantCulture) ?? "—"} | {i.Severity} | {Cell(i.Message)} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Salda domů po importu");
        sb.AppendLine();
        sb.AppendLine("| Dům | Počáteční podíl | Platby | Náklady | Saldo |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var h in r.Houses)
            sb.AppendLine($"| {Cell(h.HouseName)} | {Money(h.Opening)} | {Money(h.Payments)} | {Money(h.Costs)} | {Money(h.Saldo)} |");
        sb.AppendLine();
        sb.AppendLine("## Kontrola složek");
        sb.AppendLine();
        sb.AppendLine("| Složka | Rozpočteno | Σ domů | Sedí | Varování |");
        sb.AppendLine("|---|---:|---:|---|---|");
        foreach (var c in r.Components)
            sb.AppendLine($"| {Cell(c.ComponentName)} | {Money(c.Allocated)} | {Money(c.Houses)} | {(c.Matches ? "ano" : "**ne**")} | {Cell(string.Join("; ", c.Warnings))} |");
        return sb.ToString();
    }

    private static byte[] Xlsx(SeedImportReport r)
    {
        using var workbook = new XLWorkbook();
        Sheet(workbook, "Soubory", $"Import počátečních dat k {Day(r.Today)} — {Status(r)}",
            ["Soubor", "Nahráno", "Řádků", "Nové", "Beze změny", "Konflikty", "Chyby"],
            r.Files.Select(f => new object?[] { f.File, f.Uploaded ? "ano" : "ne", f.Rows, f.Created, f.Unchanged, f.Conflicts, f.Errors }));
        Sheet(workbook, "Problémy", "Chyby a konflikty (konflikt = záznam už existuje s jinými hodnotami)",
            ["Soubor", "Řádek", "Druh", "Popis"],
            r.Issues.Select(i => new object?[] { i.File, i.Line, i.Severity, i.Message }));
        Sheet(workbook, "Salda", $"Salda domů za {Day(r.From)} – {Day(r.Today)} (kladné = přeplatek)",
            ["Dům", "Počáteční podíl (Kč)", "Platby (Kč)", "Náklady (Kč)", "Saldo (Kč)"],
            r.Houses.Select(h => new object?[] { h.HouseName, h.Opening, h.Payments, h.Costs, h.Saldo }));
        Sheet(workbook, "Složky", "Kontrola složek: rozpočteno vs. součet přes domy",
            ["Složka", "Rozpočteno (Kč)", "Σ domů (Kč)", "Sedí", "Varování"],
            r.Components.Select(c => new object?[] { c.ComponentName, c.Allocated, c.Houses, c.Matches ? "ano" : "ne", string.Join("; ", c.Warnings) }));
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Sheet(XLWorkbook workbook, string name, string title, string[] header, IEnumerable<object?[]> rows)
    {
        var ws = workbook.Worksheets.Add(name);
        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        for (var c = 0; c < header.Length; c++)
        {
            ws.Cell(3, c + 1).Value = header[c];
            ws.Cell(3, c + 1).Style.Font.Bold = true;
        }
        var line = 4;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                var cell = ws.Cell(line, c + 1);
                switch (row[c])
                {
                    case null: break;
                    case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = "#,##0.00"; break;
                    case int i: cell.Value = i; break;
                    default: cell.Value = row[c]!.ToString(); break;
                }
            }
            line++;
        }
        ws.Columns().AdjustToContents(1, 200);
    }
}
