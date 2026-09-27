using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using Oaza.Application.DTOs;
using Oaza.Application.Ledger;
using Oaza.Domain.Enums;

namespace Oaza.Application.Tests.Ledger;

public class LedgerExportTests
{
    private static HouseLedgerResponse Ledger() => new()
    {
        HouseId = "A", HouseName = "Novákovi (142)", From = new DateOnly(2023, 11, 1), To = new DateOnly(2023, 12, 31),
        Opening = 1_000m, Payments = 1_500m, Costs = 125m, Saldo = 2_375m,
        Items =
        [
            new LedgerItemResponse { Date = new DateOnly(2023, 11, 1), Kind = LedgerItemKind.Opening, Description = "Počáteční podíl", Amount = 1_000m, Balance = 1_000m },
            new LedgerItemResponse
            {
                Date = new DateOnly(2023, 11, 1), Kind = LedgerItemKind.Cost, ComponentName = "Elektřina – vodárna", Description = "Záloha; PRE",
                Amount = -125m, Balance = 875m,
                Detail = new LedgerDetailResponse { Method = AllocationMethod.Equal, Explanation = "500 Kč rovným dílem; \"4 domy\"" },
            },
            new LedgerItemResponse { Date = new DateOnly(2023, 11, 1), Kind = LedgerItemKind.Payment, Description = "Záloha 11/2023", Amount = 1_500m, Balance = 2_375m },
        ],
    };

    [Fact]
    public void HouseCsvIsForCzechExcel()
    {
        var file = LedgerExport.House(Ledger(), "csv");

        file.FileName.Should().Be("saldo-novakovi-142-20231101-20231231.csv");
        file.ContentType.Should().StartWith("text/csv");
        file.Content.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
        var lines = Encoding.UTF8.GetString(file.Content[3..]).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("Datum;Druh;Složka;Popis;Částka (Kč);Saldo (Kč);Výpočet");
        lines[1].Should().Be("1. 11. 2023;Počáteční podíl;;Počáteční podíl;1000,00;1000,00;");
        lines[2].Should().Be("1. 11. 2023;Náklad;Elektřina – vodárna;\"Záloha; PRE\";-125,00;875,00;\"500 Kč rovným dílem; \"\"4 domy\"\"\"");
    }

    [Fact]
    public void HouseXlsxHasTitleRowsAndSummary()
    {
        var file = LedgerExport.House(Ledger(), "XLSX");

        file.FileName.Should().EndWith(".xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var ws = workbook.Worksheet("Saldo");
        ws.Cell(1, 1).GetString().Should().Contain("kladné = přeplatek");
        ws.Cell(3, 5).GetString().Should().Be("Částka (Kč)");
        ws.Cell(5, 5).GetValue<decimal>().Should().Be(-125m);
        ws.Cell(4, 1).GetDateTime().Should().Be(new DateTime(2023, 11, 1));
        ws.Cell(11, 1).GetString().Should().Be("Saldo");
        ws.Cell(11, 2).GetValue<decimal>().Should().Be(2_375m);
    }

    [Fact]
    public void OverviewHasComponentsSumsAndTheControlRow()
    {
        var overview = new LedgerOverviewResponse
        {
            From = new DateOnly(2026, 7, 1), To = new DateOnly(2026, 12, 31),
            Components = [new LedgerOverviewComponent { ComponentId = "c", ComponentName = "Osvětlení", AllocatedTotal = 1_840m, HousesTotal = 1_840m, Matches = true }],
            Houses =
            [
                new LedgerOverviewHouse { HouseName = "RD A", Costs = new() { ["c"] = 414m }, Payments = 500m, Saldo = 86m },
                new LedgerOverviewHouse { HouseName = "RD E", Costs = new() { ["c"] = 184m }, Saldo = -184m },
            ],
        };

        var csv = Encoding.UTF8.GetString(LedgerExport.Overview(overview, "csv").Content[3..]).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        csv[0].Should().Be("Dům;Počáteční podíl (Kč);Osvětlení (Kč);Platby (Kč);Saldo (Kč)");
        csv[1].Should().Be("RD A;0,00;414,00;500,00;86,00");
        csv[3].Should().Be("Σ domů;0,00;1840,00;500,00;-98,00");
        csv[4].Should().Be("Rozpočteno složkou;;1840,00;;");

        using var workbook = new XLWorkbook(new MemoryStream(LedgerExport.Overview(overview, "xlsx").Content));
        workbook.Worksheet("Přehled").Cell(5, 3).GetValue<decimal>().Should().Be(184m);
    }

    [Fact]
    public void UnknownFormatIsRejected()
    {
        var act = () => LedgerExport.House(Ledger(), "pdf");
        act.Should().Throw<ArgumentException>().WithMessage("*csv*xlsx*");
    }
}
