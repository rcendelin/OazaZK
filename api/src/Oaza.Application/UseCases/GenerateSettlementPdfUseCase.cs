using System.Globalization;
using Microsoft.Extensions.Logging;
using Oaza.Application.DTOs;
using Oaza.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPdfDocument = QuestPDF.Fluent.Document;

namespace Oaza.Application.UseCases;

public class GenerateSettlementPdfUseCase
{
    private static readonly CultureInfo CzechCulture = new("cs-CZ");
    private readonly ILogger<GenerateSettlementPdfUseCase> _logger;

    public GenerateSettlementPdfUseCase(ILogger<GenerateSettlementPdfUseCase> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates a PDF settlement sheet for a single house.
    /// </summary>
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

                page.Header().Element(header => ComposeHeader(header, period));
                page.Content().Element(content => ComposeContent(
                    content, period, house, detail, totalNetworkConsumption, totalInvoiceAmount));
                page.Footer().Element(ComposeFooter);
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, BillingPeriod period)
    {
        container.Column(column =>
        {
            column.Item().Text("Oaza Zadni Kopanina")
                .FontSize(18).Bold().FontColor(Colors.Blue.Darken3);

            column.Item().PaddingTop(4).Text("Vyuctovani vodneho")
                .FontSize(14).SemiBold().FontColor(Colors.Grey.Darken2);

            column.Item().PaddingTop(2).Text(period.Name)
                .FontSize(12).FontColor(Colors.Grey.Darken1);

            column.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Blue.Darken3);
        });
    }

    private static void ComposeContent(
        IContainer container,
        BillingPeriod period,
        House house,
        HouseSettlementDetail detail,
        decimal totalNetworkConsumption,
        decimal totalInvoiceAmount)
    {
        container.PaddingTop(16).Column(column =>
        {
            column.Spacing(12);

            // Period info section
            column.Item().Element(c => ComposePeriodInfo(c, period));

            // House info section
            column.Item().Element(c => ComposeHouseInfo(c, house));

            // Consumption table
            column.Item().Element(c => ComposeConsumptionTable(c, detail, totalNetworkConsumption));

            // Financial table
            column.Item().Element(c => ComposeFinancialTable(c, detail, totalInvoiceAmount));

            // Balance summary
            column.Item().Element(c => ComposeBalanceSummary(c, detail));
        });
    }

    private static void ComposePeriodInfo(IContainer container, BillingPeriod period)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Column(column =>
        {
            column.Item().Text("Obdobi").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken3);
            column.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("Datum od: ").SemiBold();
                    text.Span(period.DateFrom.ToString("d. MMMM yyyy", CzechCulture));
                });
                row.RelativeItem().Text(text =>
                {
                    text.Span("Datum do: ").SemiBold();
                    text.Span(period.DateTo.ToString("d. MMMM yyyy", CzechCulture));
                });
            });
        });
    }

    private static void ComposeHouseInfo(IContainer container, House house)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Column(column =>
        {
            column.Item().Text("Informace o domacnosti").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken3);
            column.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(text =>
                    {
                        text.Span("Nazev: ").SemiBold();
                        text.Span(house.Name);
                    });
                    col.Item().PaddingTop(2).Text(text =>
                    {
                        text.Span("Adresa: ").SemiBold();
                        text.Span(house.Address);
                    });
                });
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(text =>
                    {
                        text.Span("Kontaktni osoba: ").SemiBold();
                        text.Span(house.ContactPerson);
                    });
                    col.Item().PaddingTop(2).Text(text =>
                    {
                        text.Span("E-mail: ").SemiBold();
                        text.Span(house.Email);
                    });
                });
            });
        });
    }

    private static void ComposeConsumptionTable(
        IContainer container, HouseSettlementDetail detail, decimal totalNetworkConsumption)
    {
        container.Column(column =>
        {
            column.Item().Text("Spotreba").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken3);
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
                        .AlignRight().Text("Hodnota").FontColor(Colors.White).SemiBold();
                });

                AddConsumptionRow(table, "Spotreba vaseho domu",
                    FormatVolume(detail.ConsumptionM3), false);
                AddConsumptionRow(table, "Prirazena ztrata",
                    FormatVolume(detail.LossAllocatedM3), false);
                AddConsumptionRow(table, "Celkova spotreba site",
                    FormatVolume(totalNetworkConsumption), false);
                AddConsumptionRow(table, "Vas podil na celku",
                    FormatPercent(detail.SharePercent), true);
            });
        });
    }

    private static void AddConsumptionRow(TableDescriptor table, string label, string value, bool isLast)
    {
        var bgColor = isLast ? Colors.Grey.Lighten3 : Colors.White;

        table.Cell().Background(bgColor).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .Padding(6).Text(label);
        table.Cell().Background(bgColor).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .Padding(6).AlignRight().Text(value);
    }

    private static void ComposeFinancialTable(
        IContainer container, HouseSettlementDetail detail, decimal totalInvoiceAmount)
    {
        container.Column(column =>
        {
            column.Item().Text("Financni vyuctovani").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken3);
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

                AddFinancialRow(table, "Celkova faktura dodavatele",
                    FormatCurrency(totalInvoiceAmount), false);
                AddFinancialRow(table, "Vase castka k uhrade",
                    FormatCurrency(detail.CalculatedAmount), false);
                AddFinancialRow(table, "Zaplacene zalohy",
                    FormatCurrency(detail.TotalAdvances), false);
            });
        });
    }

    private static void AddFinancialRow(TableDescriptor table, string label, string value, bool highlight)
    {
        var bgColor = highlight ? Colors.Grey.Lighten3 : Colors.White;

        table.Cell().Background(bgColor).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .Padding(6).Text(label);
        table.Cell().Background(bgColor).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .Padding(6).AlignRight().Text(value);
    }

    private static void ComposeBalanceSummary(IContainer container, HouseSettlementDetail detail)
    {
        var isOverpayment = detail.Balance < 0;
        var balanceLabel = isOverpayment ? "Preplatek" : "Doplatek";
        var balanceColor = isOverpayment ? Colors.Green.Darken2 : Colors.Red.Darken2;
        var balanceAmount = Math.Abs(detail.Balance);

        container.Background(Colors.Grey.Lighten3).Padding(12).Row(row =>
        {
            row.RelativeItem().Text(balanceLabel)
                .FontSize(13).Bold().FontColor(balanceColor);
            row.RelativeItem().AlignRight().Text(FormatCurrency(balanceAmount))
                .FontSize(13).Bold().FontColor(balanceColor);
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

    private static string FormatVolume(decimal value)
    {
        return value.ToString("N2", CzechCulture) + " m\u00B3";
    }

    private static string FormatPercent(decimal value)
    {
        return value.ToString("N2", CzechCulture) + " %";
    }

    private static string FormatCurrency(decimal value)
    {
        return value.ToString("N2", CzechCulture) + " Kc";
    }
}
