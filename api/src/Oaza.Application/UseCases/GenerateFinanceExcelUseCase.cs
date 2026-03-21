using System.Globalization;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;

namespace Oaza.Application.UseCases;

public class GenerateFinanceExcelUseCase
{
    private static readonly CultureInfo CzechCulture = new("cs-CZ");
    private readonly ILogger<GenerateFinanceExcelUseCase> _logger;

    private static readonly string[] CzechMonthNames =
    {
        "Leden", "Unor", "Brezen", "Duben", "Kveten", "Cerven",
        "Cervenec", "Srpen", "Zari", "Rijen", "Listopad", "Prosinec"
    };

    private static readonly Dictionary<string, string> CategoryLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        { "voda", "Voda" },
        { "elektro", "Elektro" },
        { "udrzba", "Udrzba" },
        { "pojisteni", "Pojisteni" },
        { "jine", "Jine" },
    };

    public GenerateFinanceExcelUseCase(ILogger<GenerateFinanceExcelUseCase> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public byte[] Generate(int year, IReadOnlyList<FinancialRecord> records)
    {
        _logger.LogInformation("Generating finance Excel report for year {Year} with {Count} records.", year, records.Count);

        using var workbook = new XLWorkbook();

        // Sheet 1: Zaznamy (Records)
        ComposeRecordsSheet(workbook, records);

        // Sheet 2: Souhrn (Summary pivot by category and month)
        ComposeSummarySheet(workbook, year, records);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void ComposeRecordsSheet(XLWorkbook workbook, IReadOnlyList<FinancialRecord> records)
    {
        var ws = workbook.Worksheets.Add("Zaznamy");

        // Header row
        ws.Cell(1, 1).Value = "Datum";
        ws.Cell(1, 2).Value = "Typ";
        ws.Cell(1, 3).Value = "Kategorie";
        ws.Cell(1, 4).Value = "Popis";
        ws.Cell(1, 5).Value = "Castka";

        var headerRange = ws.Range(1, 1, 1, 5);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1565C0");
        headerRange.Style.Font.FontColor = XLColor.White;

        var sortedRecords = records.OrderBy(r => r.Date).ToList();

        for (var i = 0; i < sortedRecords.Count; i++)
        {
            var record = sortedRecords[i];
            var row = i + 2;

            ws.Cell(row, 1).Value = record.Date.ToString("dd.MM.yyyy");
            ws.Cell(row, 2).Value = record.Type == FinancialRecordType.Income ? "Prijem" : "Vydaj";
            ws.Cell(row, 3).Value = CategoryLabels.TryGetValue(record.Category, out var label) ? label : record.Category;
            ws.Cell(row, 4).Value = record.Description;
            ws.Cell(row, 5).Value = record.Amount;
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";
        }

        // Auto-fit columns
        ws.Columns().AdjustToContents();
    }

    private static void ComposeSummarySheet(XLWorkbook workbook, int year, IReadOnlyList<FinancialRecord> records)
    {
        var ws = workbook.Worksheets.Add("Souhrn");

        // Header: Kategorie | Leden | Unor | ... | Prosinec | Celkem
        ws.Cell(1, 1).Value = "Kategorie";
        for (var m = 0; m < 12; m++)
        {
            ws.Cell(1, m + 2).Value = CzechMonthNames[m];
        }
        ws.Cell(1, 14).Value = "Celkem";

        var headerRange = ws.Range(1, 1, 1, 14);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1565C0");
        headerRange.Style.Font.FontColor = XLColor.White;

        // Group by category
        var categories = records
            .Select(r => r.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();

        var currentRow = 2;

        foreach (var category in categories)
        {
            var categoryRecords = records
                .Where(r => string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var categoryLabel = CategoryLabels.TryGetValue(category, out var label) ? label : category;

            // Income row
            ws.Cell(currentRow, 1).Value = $"{categoryLabel} - Prijem";
            decimal incomeTotal = 0;
            for (var m = 1; m <= 12; m++)
            {
                var monthAmount = categoryRecords
                    .Where(r => r.Type == FinancialRecordType.Income && r.Date.Month == m)
                    .Sum(r => r.Amount);
                ws.Cell(currentRow, m + 1).Value = monthAmount;
                ws.Cell(currentRow, m + 1).Style.NumberFormat.Format = "#,##0.00";
                incomeTotal += monthAmount;
            }
            ws.Cell(currentRow, 14).Value = incomeTotal;
            ws.Cell(currentRow, 14).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(currentRow, 14).Style.Font.Bold = true;
            currentRow++;

            // Expense row
            ws.Cell(currentRow, 1).Value = $"{categoryLabel} - Vydaj";
            decimal expenseTotal = 0;
            for (var m = 1; m <= 12; m++)
            {
                var monthAmount = categoryRecords
                    .Where(r => r.Type == FinancialRecordType.Expense && r.Date.Month == m)
                    .Sum(r => r.Amount);
                ws.Cell(currentRow, m + 1).Value = monthAmount;
                ws.Cell(currentRow, m + 1).Style.NumberFormat.Format = "#,##0.00";
                expenseTotal += monthAmount;
            }
            ws.Cell(currentRow, 14).Value = expenseTotal;
            ws.Cell(currentRow, 14).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(currentRow, 14).Style.Font.Bold = true;
            currentRow++;
        }

        // Totals row
        currentRow++;
        ws.Cell(currentRow, 1).Value = "CELKEM Prijmy";
        ws.Cell(currentRow, 1).Style.Font.Bold = true;
        decimal grandIncomeTotal = 0;
        for (var m = 1; m <= 12; m++)
        {
            var monthAmount = records
                .Where(r => r.Type == FinancialRecordType.Income && r.Date.Month == m)
                .Sum(r => r.Amount);
            ws.Cell(currentRow, m + 1).Value = monthAmount;
            ws.Cell(currentRow, m + 1).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(currentRow, m + 1).Style.Font.Bold = true;
            grandIncomeTotal += monthAmount;
        }
        ws.Cell(currentRow, 14).Value = grandIncomeTotal;
        ws.Cell(currentRow, 14).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(currentRow, 14).Style.Font.Bold = true;
        currentRow++;

        ws.Cell(currentRow, 1).Value = "CELKEM Vydaje";
        ws.Cell(currentRow, 1).Style.Font.Bold = true;
        decimal grandExpenseTotal = 0;
        for (var m = 1; m <= 12; m++)
        {
            var monthAmount = records
                .Where(r => r.Type == FinancialRecordType.Expense && r.Date.Month == m)
                .Sum(r => r.Amount);
            ws.Cell(currentRow, m + 1).Value = monthAmount;
            ws.Cell(currentRow, m + 1).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(currentRow, m + 1).Style.Font.Bold = true;
            grandExpenseTotal += monthAmount;
        }
        ws.Cell(currentRow, 14).Value = grandExpenseTotal;
        ws.Cell(currentRow, 14).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(currentRow, 14).Style.Font.Bold = true;
        currentRow++;

        ws.Cell(currentRow, 1).Value = "BILANCE";
        ws.Cell(currentRow, 1).Style.Font.Bold = true;
        for (var m = 1; m <= 12; m++)
        {
            var monthIncome = records
                .Where(r => r.Type == FinancialRecordType.Income && r.Date.Month == m)
                .Sum(r => r.Amount);
            var monthExpense = records
                .Where(r => r.Type == FinancialRecordType.Expense && r.Date.Month == m)
                .Sum(r => r.Amount);
            ws.Cell(currentRow, m + 1).Value = monthIncome - monthExpense;
            ws.Cell(currentRow, m + 1).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(currentRow, m + 1).Style.Font.Bold = true;
        }
        ws.Cell(currentRow, 14).Value = grandIncomeTotal - grandExpenseTotal;
        ws.Cell(currentRow, 14).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(currentRow, 14).Style.Font.Bold = true;

        // Auto-fit columns
        ws.Columns().AdjustToContents();
    }
}
