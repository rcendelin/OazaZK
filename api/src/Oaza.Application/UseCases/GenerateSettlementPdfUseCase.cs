using System.Globalization;
using Microsoft.Extensions.Logging;
using Oaza.Application.DTOs;
using Oaza.Domain.Entities;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace Oaza.Application.UseCases;

public class GenerateSettlementPdfUseCase
{
    private static readonly CultureInfo CzechCulture = new("cs-CZ");
    private readonly ILogger<GenerateSettlementPdfUseCase> _logger;

    public GenerateSettlementPdfUseCase(ILogger<GenerateSettlementPdfUseCase> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public byte[] Generate(
        BillingPeriod period,
        House house,
        HouseSettlementDetail detail,
        decimal totalNetworkConsumption,
        decimal totalInvoiceAmount)
    {
        _logger.LogInformation(
            "Generating settlement PDF for house {HouseId} ({HouseName}), period {PeriodId}.",
            house.Id, house.Name, period.Id);

        var document = new PdfDocument();
        document.Info.Title = $"Vyuctovani - {house.Name} - {period.Name}";
        var page = document.AddPage();
        page.Size = PdfSharpCore.PageSize.A4;
        var gfx = XGraphics.FromPdfPage(page);

        var fontTitle = new XFont("Arial", 18, XFontStyle.Bold);
        var fontSubtitle = new XFont("Arial", 14, XFontStyle.Regular);
        var fontSection = new XFont("Arial", 11, XFontStyle.Bold);
        var fontNormal = new XFont("Arial", 10, XFontStyle.Regular);
        var fontBold = new XFont("Arial", 10, XFontStyle.Bold);
        var fontSmall = new XFont("Arial", 8, XFontStyle.Regular);
        var fontBalance = new XFont("Arial", 13, XFontStyle.Bold);

        var blue = XColor.FromArgb(30, 64, 175);
        var gray = XColor.FromArgb(107, 114, 128);
        var green = XColor.FromArgb(22, 163, 74);
        var red = XColor.FromArgb(220, 38, 38);
        var lightGray = XColor.FromArgb(243, 244, 246);

        double y = 50;
        double leftMargin = 55;
        double rightEdge = page.Width - 55;
        double contentWidth = rightEdge - leftMargin;

        // Header
        gfx.DrawString("Oaza Zadni Kopanina", fontTitle, new XSolidBrush(blue), leftMargin, y);
        y += 24;
        gfx.DrawString("Vyuctovani vodneho", fontSubtitle, new XSolidBrush(gray), leftMargin, y);
        y += 20;
        gfx.DrawString(period.Name, new XFont("Arial", 12, XFontStyle.Regular), new XSolidBrush(gray), leftMargin, y);
        y += 16;
        gfx.DrawLine(new XPen(blue, 1), leftMargin, y, rightEdge, y);
        y += 20;

        // Period info
        DrawSectionBox(gfx, leftMargin, ref y, contentWidth, lightGray, "Obdobi", fontSection, blue, new[]
        {
            ($"Datum od: {period.DateFrom:d. MMMM yyyy}", $"Datum do: {period.DateTo:d. MMMM yyyy}")
        }, fontNormal, fontBold);

        // House info
        DrawSectionBox(gfx, leftMargin, ref y, contentWidth, lightGray, "Informace o domacnosti", fontSection, blue, new[]
        {
            ($"Nazev: {house.Name}", $"Kontaktni osoba: {house.ContactPerson}"),
            ($"Adresa: {house.Address}", $"E-mail: {house.Email}")
        }, fontNormal, fontBold);

        // Consumption table
        y += 10;
        gfx.DrawString("Spotreba", fontSection, new XSolidBrush(blue), leftMargin, y);
        y += 16;
        DrawTableRow(gfx, leftMargin, ref y, contentWidth, blue, XColors.White, "Polozka", "Hodnota", fontBold, true);
        DrawTableRow(gfx, leftMargin, ref y, contentWidth, XColors.White, XColors.Black, "Spotreba vaseho domu", FormatVolume(detail.ConsumptionM3), fontNormal, false);
        DrawTableRow(gfx, leftMargin, ref y, contentWidth, XColors.White, XColors.Black, "Prirazena ztrata", FormatVolume(detail.LossAllocatedM3), fontNormal, false);
        DrawTableRow(gfx, leftMargin, ref y, contentWidth, XColors.White, XColors.Black, "Celkova spotreba site", FormatVolume(totalNetworkConsumption), fontNormal, false);
        DrawTableRow(gfx, leftMargin, ref y, contentWidth, new XSolidBrush(lightGray).Color, XColors.Black, "Vas podil na celku", FormatPercent(detail.SharePercent), fontBold, false);

        // Financial table
        y += 16;
        gfx.DrawString("Financni vyuctovani", fontSection, new XSolidBrush(blue), leftMargin, y);
        y += 16;
        DrawTableRow(gfx, leftMargin, ref y, contentWidth, blue, XColors.White, "Polozka", "Castka", fontBold, true);
        DrawTableRow(gfx, leftMargin, ref y, contentWidth, XColors.White, XColors.Black, "Celkova faktura dodavatele", FormatCurrency(totalInvoiceAmount), fontNormal, false);
        DrawTableRow(gfx, leftMargin, ref y, contentWidth, XColors.White, XColors.Black, "Vase castka k uhrade", FormatCurrency(detail.CalculatedAmount), fontNormal, false);
        DrawTableRow(gfx, leftMargin, ref y, contentWidth, XColors.White, XColors.Black, "Zaplacene zalohy", FormatCurrency(detail.TotalAdvances), fontNormal, false);

        // Balance summary
        y += 16;
        var isOverpayment = detail.Balance < 0;
        var balanceLabel = isOverpayment ? "Preplatek" : "Doplatek";
        var balanceColor = isOverpayment ? green : red;
        var balanceAmount = Math.Abs(detail.Balance);

        gfx.DrawRectangle(new XSolidBrush(lightGray), leftMargin, y, contentWidth, 30);
        gfx.DrawString(balanceLabel, fontBalance, new XSolidBrush(balanceColor), leftMargin + 10, y + 20);
        var amountText = FormatCurrency(balanceAmount);
        var amountWidth = gfx.MeasureString(amountText, fontBalance).Width;
        gfx.DrawString(amountText, fontBalance, new XSolidBrush(balanceColor), rightEdge - amountWidth - 10, y + 20);
        y += 40;

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

    private static void DrawSectionBox(XGraphics gfx, double x, ref double y, double width,
        XColor bgColor, string title, XFont titleFont, XColor titleColor,
        (string left, string right)[] rows, XFont normalFont, XFont boldFont)
    {
        var height = 20 + rows.Length * 18;
        gfx.DrawRectangle(new XSolidBrush(bgColor), x, y, width, height);
        gfx.DrawString(title, titleFont, new XSolidBrush(titleColor), x + 10, y + 14);
        var rowY = y + 30;
        foreach (var (left, right) in rows)
        {
            gfx.DrawString(left, normalFont, XBrushes.Black, x + 10, rowY);
            gfx.DrawString(right, normalFont, XBrushes.Black, x + width / 2, rowY);
            rowY += 18;
        }
        y += height + 12;
    }

    private static void DrawTableRow(XGraphics gfx, double x, ref double y, double width,
        XColor bgColor, XColor textColor, string col1, string col2, XFont font, bool isHeader)
    {
        var rowHeight = 22.0;
        gfx.DrawRectangle(new XSolidBrush(bgColor), x, y, width, rowHeight);
        gfx.DrawString(col1, font, new XSolidBrush(textColor), x + 8, y + 15);
        var col2Width = gfx.MeasureString(col2, font).Width;
        gfx.DrawString(col2, font, new XSolidBrush(textColor), x + width - col2Width - 8, y + 15);
        if (!isHeader)
        {
            gfx.DrawLine(new XPen(XColors.LightGray, 0.5), x, y + rowHeight, x + width, y + rowHeight);
        }
        y += rowHeight;
    }

    private static string FormatVolume(decimal value) =>
        value.ToString("N2", CzechCulture) + " m\u00B3";

    private static string FormatPercent(decimal value) =>
        value.ToString("N2", CzechCulture) + " %";

    private static string FormatCurrency(decimal value) =>
        value.ToString("N2", CzechCulture) + " Kc";
}
