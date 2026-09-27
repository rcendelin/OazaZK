using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using Oaza.Application.Seed;

namespace Oaza.Application.Tests.Seed;

public class SeedReportExportTests
{
    private static SeedImportReport Report(bool canApply, bool applied, params SeedIssue[] issues) => new()
    {
        CanApply = canApply,
        Applied = applied,
        From = new DateOnly(2023, 11, 1),
        Today = new DateOnly(2026, 12, 31),
        Files =
        [
            new SeedFileSummary(SeedFiles.Houses, true, 6, 5, 1, 0, 0),
            new SeedFileSummary(SeedFiles.Meters, false, 0, 0, 0, 0, 0),
        ],
        Issues = [.. issues],
        Houses = [new SeedHouseSaldo("RD A", 1_000m, 0m, 664m, 5_336m)],
        Components = [new SeedComponentControl("Osvětlení", 1_840m, 1_840m, true, []), new SeedComponentControl("Voda | PVK", 10m, 9m, false, ["chybí odečet"])],
    };

    [Fact]
    public void Markdown_HasFilesIssuesSaldaAndControls()
    {
        var file = SeedReportExport.Build(Report(false, false, new SeedIssue(SeedFiles.Houses, 3, SeedIssue.Conflict, "Dům „RD A“ už existuje")), "md");

        file.FileName.Should().Be("import-pocatecnich-dat-20261231.md");
        var text = Encoding.UTF8.GetString(file.Content);
        text.Should().Contain("**Nelze zapsat — opravte chyby a konflikty.**")
            .And.Contain("| houses.csv | 6 | 5 | 1 | 0 | 0 |")
            .And.Contain("| meters.csv | nenahráno |")
            .And.Contain("| houses.csv | 3 | konflikt | Dům „RD A“ už existuje |")
            .And.Contain("| RD A | 1 000,00 | 0,00 | 664,00 | 5 336,00 |")
            .And.Contain("| Voda \\| PVK | 10,00 | 9,00 | **ne** | chybí odečet |");
    }

    [Theory]
    [InlineData(true, false, "Zkouška prošla — lze zapsat.")]
    [InlineData(false, true, "Zapsáno.")]
    [InlineData(false, false, "Není co zapsat — vše už existuje.")]
    public void Markdown_StatusLine(bool canApply, bool applied, string status)
    {
        var text = Encoding.UTF8.GetString(SeedReportExport.Build(Report(canApply, applied), "md").Content);

        text.Should().Contain($"**{status}**").And.Contain("Žádné.");
    }

    [Fact]
    public void Xlsx_HasFourSheets()
    {
        var file = SeedReportExport.Build(Report(true, false, new SeedIssue("x.csv", null, SeedIssue.Error, "neznámý soubor")), "xlsx");

        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        workbook.Worksheets.Select(w => w.Name).Should().Equal("Soubory", "Problémy", "Salda", "Složky");
        workbook.Worksheet("Salda").Cell(4, 5).GetValue<decimal>().Should().Be(5_336m);
        workbook.Worksheet("Problémy").Cell(4, 2).IsEmpty().Should().BeTrue();
        workbook.Worksheet("Soubory").Cell(5, 2).GetString().Should().Be("ne");
    }

    [Fact]
    public void UnknownFormat_IsRejected() =>
        FluentActions.Invoking(() => SeedReportExport.Build(Report(true, false), "pdf")).Should().Throw<ArgumentException>();
}
