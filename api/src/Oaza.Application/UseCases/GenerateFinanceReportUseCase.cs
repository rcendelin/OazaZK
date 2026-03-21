using System.Globalization;
using Microsoft.Extensions.Logging;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPdfDocument = QuestPDF.Fluent.Document;

namespace Oaza.Application.UseCases;

public class GenerateFinanceReportUseCase
{
    private static readonly CultureInfo CzechCulture = new("cs-CZ");
    private readonly ILogger<GenerateFinanceReportUseCase> _logger;

    private static readonly Dictionary<string, string> CategoryLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        { "voda", "Voda" },
        { "elektro", "Elektro" },
        { "udrzba", "Udrzba" },
        { "pojisteni", "Pojisteni" },
        { "jine", "Jine" },
    };

    private record CategoryBreakdownItem(string Category, decimal Income, decimal Expenses);

    public GenerateFinanceReportUseCase(ILogger<GenerateFinanceReportUseCase> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public byte[] Generate(int year, IReadOnlyList<FinancialRecord> records)
    {
        _logger.LogInformation("Generating finance report PDF for year {Year} with {Count} records.", year, records.Count);

        var totalIncome = records.Where(r => r.Type == FinancialRecordType.Income).Sum(r => r.Amount);
        var totalExpenses = records.Where(r => r.Type == FinancialRecordType.Expense).Sum(r => r.Amount);
        var balance = totalIncome - totalExpenses;

        var categoryBreakdown = records
            .GroupBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CategoryBreakdownItem(
                Category: g.Key,
                Income: g.Where(r => r.Type == FinancialRecordType.Income).Sum(r => r.Amount),
                Expenses: g.Where(r => r.Type == FinancialRecordType.Expense).Sum(r => r.Amount)))
            .OrderBy(c => c.Category)
            .ToList();

        var sortedRecords = records.OrderBy(r => r.Date).ToList();

        var document = QuestPdfDocument.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.5f, Unit.Centimetre);
                page.MarginBottom(1.5f, Unit.Centimetre);
                page.MarginLeft(2f, Unit.Centimetre);
                page.MarginRight(2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(header => ComposeHeader(header, year));
                page.Content().Element(content => ComposeContent(
                    content, totalIncome, totalExpenses, balance,
                    categoryBreakdown, sortedRecords));
                page.Footer().Element(ComposeFooter);
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, int year)
    {
        container.Column(column =>
        {
            column.Item().Text("Oaza Zadni Kopanina")
                .FontSize(18).Bold().FontColor(Colors.Blue.Darken3);

            column.Item().PaddingTop(4).Text($"Hospodareni za rok {year}")
                .FontSize(14).SemiBold().FontColor(Colors.Grey.Darken2);

            column.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Blue.Darken3);
        });
    }

    private static void ComposeContent(
        IContainer container,
        decimal totalIncome,
        decimal totalExpenses,
        decimal balance,
        List<CategoryBreakdownItem> categoryBreakdown,
        List<FinancialRecord> sortedRecords)
    {
        container.PaddingTop(16).Column(column =>
        {
            column.Spacing(12);

            // Summary section
            column.Item().Element(c => ComposeSummary(c, totalIncome, totalExpenses, balance));

            // Category breakdown table
            column.Item().Element(c => ComposeCategoryTable(c, categoryBreakdown));

            // Detail records table
            column.Item().Element(c => ComposeDetailTable(c, sortedRecords));
        });
    }

    private static void ComposeSummary(IContainer container, decimal totalIncome, decimal totalExpenses, decimal balance)
    {
        container.Column(column =>
        {
            column.Item().Text("Souhrn").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken3);
            column.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Blue.Darken3).Padding(6)
                        .Text("Polozka").FontColor(Colors.White).SemiBold();
                    header.Cell().Background(Colors.Blue.Darken3).Padding(6)
                        .AlignRight().Text("Castka").FontColor(Colors.White).SemiBold();
                });

                AddSummaryRow(table, "Celkove prijmy", FormatCurrency(totalIncome), Colors.White);
                AddSummaryRow(table, "Celkove vydaje", FormatCurrency(totalExpenses), Colors.White);
                AddSummaryRow(table, "Bilance", FormatCurrency(balance),
                    balance >= 0 ? Colors.Green.Lighten4 : Colors.Red.Lighten4);
            });
        });
    }

    private static void AddSummaryRow(TableDescriptor table, string label, string value, string bgColor)
    {
        table.Cell().Background(bgColor).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .Padding(6).Text(label);
        table.Cell().Background(bgColor).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .Padding(6).AlignRight().Text(value);
    }

    private static void ComposeCategoryTable(IContainer container, List<CategoryBreakdownItem> categories)
    {
        container.Column(column =>
        {
            column.Item().Text("Rozdeleni dle kategorii").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken3);
            column.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Blue.Darken3).Padding(6)
                        .Text("Kategorie").FontColor(Colors.White).SemiBold();
                    header.Cell().Background(Colors.Blue.Darken3).Padding(6)
                        .AlignRight().Text("Prijmy").FontColor(Colors.White).SemiBold();
                    header.Cell().Background(Colors.Blue.Darken3).Padding(6)
                        .AlignRight().Text("Vydaje").FontColor(Colors.White).SemiBold();
                    header.Cell().Background(Colors.Blue.Darken3).Padding(6)
                        .AlignRight().Text("Bilance").FontColor(Colors.White).SemiBold();
                });

                foreach (var cat in categories)
                {
                    var catBalance = cat.Income - cat.Expenses;
                    var label = CategoryLabels.TryGetValue(cat.Category, out var l) ? l : cat.Category;

                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(label);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
                        .AlignRight().Text(FormatCurrency(cat.Income));
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
                        .AlignRight().Text(FormatCurrency(cat.Expenses));
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6)
                        .AlignRight().Text(FormatCurrency(catBalance));
                }
            });
        });
    }

    private static void ComposeDetailTable(IContainer container, List<FinancialRecord> records)
    {
        container.Column(column =>
        {
            column.Item().Text("Detailni zaznamy").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken3);
            column.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);  // Datum
                    columns.RelativeColumn(1);  // Typ
                    columns.RelativeColumn(2);  // Kategorie
                    columns.RelativeColumn(4);  // Popis
                    columns.RelativeColumn(2);  // Castka
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Blue.Darken3).Padding(6)
                        .Text("Datum").FontColor(Colors.White).SemiBold();
                    header.Cell().Background(Colors.Blue.Darken3).Padding(6)
                        .Text("Typ").FontColor(Colors.White).SemiBold();
                    header.Cell().Background(Colors.Blue.Darken3).Padding(6)
                        .Text("Kategorie").FontColor(Colors.White).SemiBold();
                    header.Cell().Background(Colors.Blue.Darken3).Padding(6)
                        .Text("Popis").FontColor(Colors.White).SemiBold();
                    header.Cell().Background(Colors.Blue.Darken3).Padding(6)
                        .AlignRight().Text("Castka").FontColor(Colors.White).SemiBold();
                });

                foreach (var record in records)
                {
                    var typeLabel = record.Type == FinancialRecordType.Income ? "Prijem" : "Vydaj";
                    var categoryLabel = CategoryLabels.TryGetValue(record.Category, out var cl) ? cl : record.Category;

                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                        .Text(record.Date.ToString("dd.MM.yyyy")).FontSize(9);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                        .Text(typeLabel).FontSize(9);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                        .Text(categoryLabel).FontSize(9);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                        .Text(record.Description).FontSize(9);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                        .AlignRight().Text(FormatCurrency(record.Amount)).FontSize(9);
                }
            });
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            column.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("Datum vystaveni: ").FontSize(8);
                    text.Span(DateTime.UtcNow.ToString("d. MMMM yyyy", CzechCulture)).FontSize(8);
                });
                row.RelativeItem().AlignRight()
                    .Text("Vygenerovano portalem Oaza Zadni Kopanina")
                    .FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private static string FormatCurrency(decimal value)
    {
        return value.ToString("N2", CzechCulture) + " Kc";
    }
}
