using System.Globalization;
using Microsoft.Extensions.Logging;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

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
            .Select(g => (
                Category: g.Key,
                Income: g.Where(r => r.Type == FinancialRecordType.Income).Sum(r => r.Amount),
                Expenses: g.Where(r => r.Type == FinancialRecordType.Expense).Sum(r => r.Amount)))
            .OrderBy(c => c.Category)
            .ToList();

        var sortedRecords = records.OrderBy(r => r.Date).ToList();

        var document = new PdfDocument();
        document.Info.Title = $"Hospodareni - {year}";
        var page = document.AddPage();
        page.Size = PdfSharpCore.PageSize.A4;
        var gfx = XGraphics.FromPdfPage(page);

        var fontTitle = new XFont("Arial", 18, XFontStyle.Bold);
        var fontSubtitle = new XFont("Arial", 14, XFontStyle.Regular);
        var fontSection = new XFont("Arial", 11, XFontStyle.Bold);
        var fontNormal = new XFont("Arial", 10, XFontStyle.Regular);
        var fontBold = new XFont("Arial", 10, XFontStyle.Bold);
        var fontSmall = new XFont("Arial", 8, XFontStyle.Regular);
        var fontDetail = new XFont("Arial", 9, XFontStyle.Regular);

        var blue = XColor.FromArgb(30, 64, 175);
        var gray = XColor.FromArgb(107, 114, 128);
        var lightGray = XColor.FromArgb(243, 244, 246);

        double y = 50;
        double leftMargin = 55;
        double rightEdge = page.Width - 55;
        double contentWidth = rightEdge - leftMargin;

        // Header
        gfx.DrawString("Oaza Zadni Kopanina", fontTitle, new XSolidBrush(blue), leftMargin, y);
        y += 24;
        gfx.DrawString($"Hospodareni za rok {year}", fontSubtitle, new XSolidBrush(gray), leftMargin, y);
        y += 16;
        gfx.DrawLine(new XPen(blue, 1), leftMargin, y, rightEdge, y);
        y += 20;

        // Summary table
        gfx.DrawString("Souhrn", fontSection, new XSolidBrush(blue), leftMargin, y);
        y += 16;
        DrawRow(gfx, leftMargin, ref y, contentWidth, blue, XColors.White, "Polozka", "Castka", fontBold, true);
        DrawRow(gfx, leftMargin, ref y, contentWidth, XColors.White, XColors.Black, "Celkove prijmy", FormatCurrency(totalIncome), fontNormal, false);
        DrawRow(gfx, leftMargin, ref y, contentWidth, XColors.White, XColors.Black, "Celkove vydaje", FormatCurrency(totalExpenses), fontNormal, false);
        var balColor = balance >= 0 ? XColor.FromArgb(22, 163, 74) : XColor.FromArgb(220, 38, 38);
        DrawRow(gfx, leftMargin, ref y, contentWidth, lightGray, balColor, "Bilance", FormatCurrency(balance), fontBold, false);

        // Category breakdown
        y += 16;
        gfx.DrawString("Rozdeleni dle kategorii", fontSection, new XSolidBrush(blue), leftMargin, y);
        y += 16;

        // Category header
        var colWidths = new[] { contentWidth * 0.34, contentWidth * 0.22, contentWidth * 0.22, contentWidth * 0.22 };
        DrawCategoryHeader(gfx, leftMargin, ref y, colWidths, blue, fontBold);

        foreach (var cat in categoryBreakdown)
        {
            var catBalance = cat.Income - cat.Expenses;
            var label = CategoryLabels.TryGetValue(cat.Category, out var l) ? l : cat.Category;
            DrawCategoryRow(gfx, leftMargin, ref y, colWidths, label,
                FormatCurrency(cat.Income), FormatCurrency(cat.Expenses), FormatCurrency(catBalance), fontNormal);
        }

        // Detail records
        y += 16;
        gfx.DrawString("Detailni zaznamy", fontSection, new XSolidBrush(blue), leftMargin, y);
        y += 16;

        var detColWidths = new[] { contentWidth * 0.15, contentWidth * 0.10, contentWidth * 0.15, contentWidth * 0.40, contentWidth * 0.20 };
        DrawDetailHeader(gfx, leftMargin, ref y, detColWidths, blue, fontBold);

        foreach (var record in sortedRecords)
        {
            if (y > page.Height - 60)
            {
                // New page
                page = document.AddPage();
                page.Size = PdfSharpCore.PageSize.A4;
                gfx = XGraphics.FromPdfPage(page);
                y = 50;
                DrawDetailHeader(gfx, leftMargin, ref y, detColWidths, blue, fontBold);
            }

            var typeLabel = record.Type == FinancialRecordType.Income ? "Prijem" : "Vydaj";
            var categoryLabel = CategoryLabels.TryGetValue(record.Category, out var cl) ? cl : record.Category;
            var desc = record.Description.Length > 40 ? record.Description[..40] + "..." : record.Description;

            DrawDetailRow(gfx, leftMargin, ref y, detColWidths,
                record.Date.ToString("dd.MM.yyyy"), typeLabel, categoryLabel, desc,
                FormatCurrency(record.Amount), fontDetail);
        }

        // Footer
        y = page.Height - 40;
        gfx.DrawLine(new XPen(XColors.LightGray, 0.5), leftMargin, y, rightEdge, y);
        y += 12;
        gfx.DrawString($"Datum vystaveni: {DateTime.UtcNow.ToString("d. MMMM yyyy", CzechCulture)}", fontSmall, XBrushes.Gray, leftMargin, y);
        var footerText = "Vygenerovano portalem Oaza Zadni Kopanina";
        var footerWidth = gfx.MeasureString(footerText, fontSmall).Width;
        gfx.DrawString(footerText, fontSmall, XBrushes.Gray, rightEdge - footerWidth, y);

        using var ms = new MemoryStream();
        document.Save(ms, false);
        return ms.ToArray();
    }

    private static void DrawRow(XGraphics gfx, double x, ref double y, double width,
        XColor bgColor, XColor textColor, string col1, string col2, XFont font, bool isHeader)
    {
        var rowHeight = 22.0;
        gfx.DrawRectangle(new XSolidBrush(bgColor), x, y, width, rowHeight);
        gfx.DrawString(col1, font, new XSolidBrush(textColor), x + 8, y + 15);
        var col2Width = gfx.MeasureString(col2, font).Width;
        gfx.DrawString(col2, font, new XSolidBrush(textColor), x + width - col2Width - 8, y + 15);
        if (!isHeader) gfx.DrawLine(new XPen(XColors.LightGray, 0.5), x, y + rowHeight, x + width, y + rowHeight);
        y += rowHeight;
    }

    private static void DrawCategoryHeader(XGraphics gfx, double x, ref double y, double[] widths, XColor bg, XFont font)
    {
        var headers = new[] { "Kategorie", "Prijmy", "Vydaje", "Bilance" };
        var cx = x;
        for (var i = 0; i < 4; i++)
        {
            gfx.DrawRectangle(new XSolidBrush(bg), cx, y, widths[i], 22);
            gfx.DrawString(headers[i], font, XBrushes.White, cx + 6, y + 15);
            cx += widths[i];
        }
        y += 22;
    }

    private static void DrawCategoryRow(XGraphics gfx, double x, ref double y, double[] widths,
        string col1, string col2, string col3, string col4, XFont font)
    {
        var vals = new[] { col1, col2, col3, col4 };
        var cx = x;
        for (var i = 0; i < 4; i++)
        {
            gfx.DrawString(vals[i], font, XBrushes.Black, cx + 6, y + 15);
            cx += widths[i];
        }
        gfx.DrawLine(new XPen(XColors.LightGray, 0.5), x, y + 20, x + widths.Sum(), y + 20);
        y += 20;
    }

    private static void DrawDetailHeader(XGraphics gfx, double x, ref double y, double[] widths, XColor bg, XFont font)
    {
        var headers = new[] { "Datum", "Typ", "Kategorie", "Popis", "Castka" };
        var cx = x;
        for (var i = 0; i < 5; i++)
        {
            gfx.DrawRectangle(new XSolidBrush(bg), cx, y, widths[i], 20);
            gfx.DrawString(headers[i], font, XBrushes.White, cx + 4, y + 14);
            cx += widths[i];
        }
        y += 20;
    }

    private static void DrawDetailRow(XGraphics gfx, double x, ref double y, double[] widths,
        string col1, string col2, string col3, string col4, string col5, XFont font)
    {
        var vals = new[] { col1, col2, col3, col4, col5 };
        var cx = x;
        for (var i = 0; i < 5; i++)
        {
            gfx.DrawString(vals[i], font, XBrushes.Black, cx + 4, y + 13);
            cx += widths[i];
        }
        gfx.DrawLine(new XPen(XColors.LightGray, 0.5), x, y + 18, x + widths.Sum(), y + 18);
        y += 18;
    }

    private static string FormatCurrency(decimal value) =>
        value.ToString("N2", CzechCulture) + " Kc";
}
