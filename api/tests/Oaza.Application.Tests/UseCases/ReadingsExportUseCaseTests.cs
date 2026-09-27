using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using Oaza.Application.Readings;
using Oaza.Application.Tests.TestSupport;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;

namespace Oaza.Application.Tests.UseCases;

public class ReadingsExportUseCaseTests
{
    private readonly MemoryMeters _meters = new();
    private readonly MemoryReadings _readings = new();
    private readonly MemoryHouses _houses = new();

    public ReadingsExportUseCaseTests()
    {
        _houses.UpsertAsync(new House { Id = "h1", Name = "RD Novákovi", IsActive = true });
        _meters.UpsertAsync(new WaterMeter { Id = "m1", MeterNumber = "V-142", Type = MeterType.Individual, HouseId = "h1" });
        _meters.UpsertAsync(new WaterMeter { Id = "main", MeterNumber = "H-1", Type = MeterType.Main });
        Read("m1", new DateOnly(2026, 1, 1), 100m);
        Read("m1", new DateOnly(2026, 3, 15), 112.345m, "interpolace mezi 1. 3. a 1. 4. 2026");
        Read("m1", new DateOnly(2026, 6, 1), 120m);
        Read("main", new DateOnly(2026, 3, 1), 5000m);
    }

    private void Read(string meter, DateOnly day, decimal value, string? estimate = null) =>
        _readings.UpsertAsync(new MeterReading
        {
            MeterId = meter, ReadingDate = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Value = value,
            Source = ReadingSource.Manual, IsEstimate = estimate is not null, EstimateNote = estimate,
        });

    private ReadingsExportUseCase Sut() => new(_meters, _readings, _houses);

    [Fact]
    public async Task Csv_MarksEstimatesAndCountsConsumptionFromTheReadingBeforeTheRange()
    {
        var file = await Sut().ExportAsync(new DateOnly(2026, 2, 1), new DateOnly(2026, 12, 31), "csv");

        file.FileName.Should().Be("odecty-20260201-20261231.csv");
        var lines = Encoding.UTF8.GetString(file.Content).TrimStart('﻿').Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().Equal(
            "Datum;Vodoměr;Dům;Stav (m³);Spotřeba od předchozího (m³);Odhad;Popis odhadu;Zdroj",
            "1. 3. 2026;H-1;hlavní vodoměr;5000,000;;ne;;ručně",
            "15. 3. 2026;V-142;RD Novákovi;112,345;12,345;ano;interpolace mezi 1. 3. a 1. 4. 2026;ručně",
            "1. 6. 2026;V-142;RD Novákovi;120,000;7,655;ne;;ručně");
    }

    [Fact]
    public async Task Xlsx_WithoutRange_ContainsEveryReading()
    {
        var file = await Sut().ExportAsync(null, null, "xlsx");

        file.FileName.Should().Be("odecty-zacatek-dnes.xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var ws = workbook.Worksheet("Odečty");
        ws.Cell(1, 1).GetString().Should().Contain("všechny");
        ws.Cell(5, 4).GetValue<decimal>().Should().Be(100m);
        ws.Cell(6, 6).GetString().Should().Be("ano");
        ws.Cell(6, 4).Style.NumberFormat.Format.Should().Be("#,##0.000");
        ws.LastRowUsed()!.RowNumber().Should().Be(7);
    }

    [Fact]
    public async Task InvalidInput_IsRejected()
    {
        await Sut().Invoking(s => s.ExportAsync(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1), "csv")).Should().ThrowAsync<ArgumentException>();
        await Sut().Invoking(s => s.ExportAsync(null, null, "pdf")).Should().ThrowAsync<ArgumentException>();
    }
}
