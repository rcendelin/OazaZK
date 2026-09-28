using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Oaza.Application.Pdf;
using Oaza.Application.Tests.TestSupport;
using Oaza.Application.UseCases;
using Oaza.Domain.Entities;
using Oaza.Domain.Time;
using PdfSharpCore.Fonts;

namespace Oaza.Application.Tests.Pdf;

/// <summary>PDF exports must not depend on system fonts (the Flex Consumption image has none — finance PDF was 500).</summary>
public class PdfFontsTests
{
    [Fact]
    public void EmbeddedFonts_AreRegisteredAndLoadable()
    {
        PdfFonts.EnsureRegistered();
        PdfFonts.EnsureRegistered();

        GlobalFontSettings.FontResolver.Should().BeOfType<PdfFonts>();
        var resolver = new PdfFonts();
        resolver.DefaultFontName.Should().Be(PdfFonts.Family);
        resolver.ResolveTypeface("Arial", isBold: true, isItalic: false).FaceName.Should().Be("DejaVuSans-Bold");
        var regular = resolver.ResolveTypeface("Whatever", isBold: false, isItalic: true);
        regular.FaceName.Should().Be("DejaVuSans");
        resolver.GetFont("DejaVuSans").Should().HaveCountGreaterThan(100_000);
        resolver.GetFont("DejaVuSans-Bold").Should().HaveCountGreaterThan(100_000);
        FluentActions.Invoking(() => resolver.GetFont("Missing")).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FinanceReportPdf_UsesTheEmbeddedFont_WithCzechText()
    {
        var useCase = new GenerateFinanceReportUseCase(NullLogger<GenerateFinanceReportUseCase>.Instance, new PragueClock(new FixedTimeProvider(DateTimeOffset.UtcNow)));

        var pdf = useCase.Generate(2026, [new FinancialRecord { Id = "r1", Year = 2026, Category = "údržba", Amount = 1234.5m, Date = new DateTime(2026, 5, 1), Description = "Sekání trávy — Žluťoučký kůň" }]);

        var text = Encoding.Latin1.GetString(pdf);
        text.Should().StartWith("%PDF");
        var fonts = System.Text.RegularExpressions.Regex.Matches(text, @"/BaseFont\s*/\S+").Select(m => m.Value).ToList();
        fonts.Should().NotBeEmpty().And.OnlyContain(f => f.Contains("DejaVu"), "no system font may be used: {0}", string.Join(", ", fonts));
    }
}
