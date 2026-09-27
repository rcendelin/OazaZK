using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Oaza.Application.DTOs;
using Oaza.Application.UseCases;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.UseCases;

public class ImportReadingsUseCaseTests
{
    private readonly Mock<IMeterReadingRepository> _readingRepoMock;
    private readonly Mock<IWaterMeterRepository> _meterRepoMock;
    private readonly Mock<ILogger<ImportReadingsUseCase>> _loggerMock;
    private readonly ImportReadingsUseCase _useCase;

    private readonly WaterMeter _mainMeter;
    private readonly WaterMeter _houseMeter1;
    private readonly WaterMeter _houseMeter2;

    public ImportReadingsUseCaseTests()
    {
        _readingRepoMock = new Mock<IMeterReadingRepository>();
        _meterRepoMock = new Mock<IWaterMeterRepository>();
        _loggerMock = new Mock<ILogger<ImportReadingsUseCase>>();

        _useCase = new ImportReadingsUseCase(
            _readingRepoMock.Object,
            _meterRepoMock.Object,
            _loggerMock.Object);

        _mainMeter = new WaterMeter
        {
            Id = "meter-main",
            MeterNumber = "MAIN-001",
            Type = MeterType.Main,
            HouseId = null,
            InstallationDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        _houseMeter1 = new WaterMeter
        {
            Id = "meter-house1",
            MeterNumber = "IND-001",
            Type = MeterType.Individual,
            HouseId = "house-1",
            InstallationDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        _houseMeter2 = new WaterMeter
        {
            Id = "meter-house2",
            MeterNumber = "IND-002",
            Type = MeterType.Individual,
            HouseId = "house-2",
            InstallationDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    [Fact]
    public async Task ParseAndValidateAsync_ValidExcel_ReturnsPreviewWithNoErrors()
    {
        // Arrange
        var meters = new List<WaterMeter> { _mainMeter, _houseMeter1, _houseMeter2 };
        SetupMeters(meters);
        SetupEmptyReadings(meters);

        var stream = CreateExcelStream(
            dates: new[] { new DateTime(2026, 1, 15) },
            meterRows: new[]
            {
                new MeterRow("MAIN-001", new object[] { 100.5m }),
                new MeterRow("IND-001", new object[] { 30.2m }),
                new MeterRow("IND-002", new object[] { 25.1m })
            });

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Errors.Should().BeEmpty();
        result.Rows.Should().HaveCount(1);
        result.Rows[0].MeterValues.Should().HaveCount(3);
        result.Rows[0].MeterValues["meter-main"].Should().Be(100.5m);
        result.Rows[0].MeterValues["meter-house1"].Should().Be(30.2m);
        result.Rows[0].MeterValues["meter-house2"].Should().Be(25.1m);

    }

    [Fact]
    public async Task ParseClipboardAndValidateAsync_MapsByRadioAddress_UsesValue1_AndChosenDate()
    {
        // Arrange: meters carry their physical radio addresses
        _mainMeter.RadioAddress = "22040724";
        _houseMeter1.RadioAddress = "22040725";
        _houseMeter2.RadioAddress = "22040726";
        var meters = new List<WaterMeter> { _mainMeter, _houseMeter1, _houseMeter2 };
        SetupMeters(meters);
        SetupEmptyReadings(meters);

        var date = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var text = string.Join("\n", new[]
        {
            "Reception time\tMode\tManuf.\tAddress\tCount\tSignal [%]\tValue 1\tUnit 1\tValue 4\tUnit 4",
            "2026-06-12 00:08:49\tT1\tELR\t22040724\t4\t54\t426,576\tm3\t426,545\tm3",
            "2026-06-12 00:08:43\tT1\tELR\t22040725\t3\t47\t306,552\tm3\t305,128\tm3",
            "2026-06-12 00:09:42\tT1\tELR\t22040726\t7\t40\t723,061\tm3\t715,975\tm3",
        });

        // Act
        var result = await _useCase.ParseClipboardAndValidateAsync(text, date, "user-1");

        // Assert: matched by Address, value taken from Value 1 (Czech comma), assigned the chosen date
        result.Errors.Should().BeEmpty();
        result.Rows.Should().HaveCount(1);
        result.Rows[0].ReadingDate.Should().Be(date);
        result.Rows[0].MeterValues.Should().HaveCount(3);
        result.Rows[0].MeterValues["meter-main"].Should().Be(426.576m);
        result.Rows[0].MeterValues["meter-house1"].Should().Be(306.552m);
        result.Rows[0].MeterValues["meter-house2"].Should().Be(723.061m);
    }

    [Fact]
    public async Task ParseClipboardAndValidateAsync_UnknownAddress_ReturnsError()
    {
        // Arrange
        _mainMeter.RadioAddress = "22040724";
        var meters = new List<WaterMeter> { _mainMeter };
        SetupMeters(meters);
        SetupEmptyReadings(meters);

        var text = "Address\tValue 1\tUnit 1\n99999999\t100,5\tm3";

        // Act
        var result = await _useCase.ParseClipboardAndValidateAsync(
            text, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), "user-1");

        // Assert
        result.Errors.Should().ContainSingle();
        result.Errors[0].Message.Should().Contain("neodpovídá");
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseClipboardAndValidateAsync_MatchesByMeterNumber_WhenAddressFieldEmpty()
    {
        // Admin put the physical address straight into the identifier (MeterNumber),
        // leaving the dedicated Address field empty — the import must still match.
        _mainMeter.MeterNumber = "22040724";
        _mainMeter.RadioAddress = null;
        var meters = new List<WaterMeter> { _mainMeter };
        SetupMeters(meters);
        SetupEmptyReadings(meters);

        var text = "Address\tValue 1\tUnit 1\n22040724\t426,576\tm3";

        // Act
        var result = await _useCase.ParseClipboardAndValidateAsync(
            text, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), "user-1");

        // Assert
        result.Errors.Should().BeEmpty();
        result.Rows.Should().ContainSingle();
        result.Rows[0].MeterValues["meter-main"].Should().Be(426.576m);
    }

    [Fact]
    public async Task ParseAndValidateAsync_DuplicateReading_ReturnsError()
    {
        // Arrange
        var meters = new List<WaterMeter> { _mainMeter };
        SetupMeters(meters);

        // Existing reading for 2026-01-15 (exact date match)
        _readingRepoMock.Setup(r => r.GetByMeterIdAsync("meter-main"))
            .ReturnsAsync(new List<MeterReading>
            {
                new()
                {
                    MeterId = "meter-main",
                    ReadingDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                    Value = 90m,
                    Source = ReadingSource.Manual,
                    ImportedAt = DateTime.UtcNow,
                    ImportedBy = "user-1"
                }
            });

        var stream = CreateExcelStream(
            dates: new[] { new DateTime(2026, 1, 15) },
            meterRows: new[]
            {
                new MeterRow("MAIN-001", new object[] { 100m })
            });

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Type.Should().Be("error");
        result.Errors[0].Message.Should().Contain("již existuje");
    }

    [Fact]
    public async Task ParseAndValidateAsync_SameMonthDifferentDay_ReturnsError()
    {
        // Arrange: existing reading on 2026-01-05, import a new one on 2026-01-20.
        // The duplicate rule is per MONTH, so the preview must flag this (previously
        // it slipped through preview and only failed at confirm time).
        var meters = new List<WaterMeter> { _mainMeter };
        SetupMeters(meters);

        _readingRepoMock.Setup(r => r.GetByMeterIdAsync("meter-main"))
            .ReturnsAsync(new List<MeterReading>
            {
                new()
                {
                    MeterId = "meter-main",
                    ReadingDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                    Value = 90m,
                    Source = ReadingSource.Manual,
                    ImportedAt = DateTime.UtcNow,
                    ImportedBy = "user-1"
                }
            });

        var stream = CreateExcelStream(
            dates: new[] { new DateTime(2026, 1, 20) },
            meterRows: new[]
            {
                new MeterRow("MAIN-001", new object[] { 100m })
            });

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Type.Should().Be("error");
        result.Errors[0].Message.Should().Contain("již existuje");
    }

    [Fact]
    public async Task ParseAndValidateAsync_NegativeConsumption_ReturnsError()
    {
        // Arrange
        var meters = new List<WaterMeter> { _mainMeter };
        SetupMeters(meters);

        // Existing reading with value 100
        _readingRepoMock.Setup(r => r.GetByMeterIdAsync("meter-main"))
            .ReturnsAsync(new List<MeterReading>
            {
                new()
                {
                    MeterId = "meter-main",
                    ReadingDate = new DateTime(2025, 12, 15, 0, 0, 0, DateTimeKind.Utc),
                    Value = 100m,
                    Source = ReadingSource.Manual,
                    ImportedAt = DateTime.UtcNow,
                    ImportedBy = "user-1"
                }
            });

        // Import with value 90 (less than previous 100)
        var stream = CreateExcelStream(
            dates: new[] { new DateTime(2026, 1, 15) },
            meterRows: new[]
            {
                new MeterRow("MAIN-001", new object[] { 90m })
            });

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("Záporná spotřeba");
    }

    [Fact]
    public async Task ParseAndValidateAsync_MissingMeterValue_ReturnsWarning()
    {
        // Arrange
        var meters = new List<WaterMeter> { _mainMeter, _houseMeter1 };
        SetupMeters(meters);
        SetupEmptyReadings(meters);

        // Transposed format: MAIN-001 has a value, IND-001 has an empty cell
        var stream = CreateExcelWithMissingValue(
            date: new DateTime(2026, 1, 15),
            meterWithValue: "MAIN-001",
            mainValue: 100m,
            meterWithMissing: "IND-001");

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Warnings.Should().Contain(w => w.Message.Contains("Chybí hodnota"));
    }

    [Fact]
    public async Task ParseAndValidateAsync_AnomalyDetected_ReturnsWarning()
    {
        // Arrange
        var meters = new List<WaterMeter> { _mainMeter };
        SetupMeters(meters);

        // Existing readings with consistent low consumption (~5 per month)
        var existingReadings = new List<MeterReading>();
        for (var i = 0; i < 7; i++)
        {
            existingReadings.Add(new MeterReading
            {
                MeterId = "meter-main",
                ReadingDate = new DateTime(2025, 6 + i, 15, 0, 0, 0, DateTimeKind.Utc),
                Value = 50 + (i * 5),
                Source = ReadingSource.Import,
                ImportedAt = DateTime.UtcNow,
                ImportedBy = "user-1"
            });
        }

        _readingRepoMock.Setup(r => r.GetByMeterIdAsync("meter-main"))
            .ReturnsAsync(existingReadings);

        // Import with huge consumption (previous was 80, now 200 -> consumption 120, avg ~5)
        var stream = CreateExcelStream(
            dates: new[] { new DateTime(2026, 2, 15) },
            meterRows: new[]
            {
                new MeterRow("MAIN-001", new object[] { 200m })
            });

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Warnings.Should().Contain(w => w.Message.Contains("Anomálie"));
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAndValidateAsync_UnmatchedMeterNumber_ReturnsError()
    {
        // Arrange
        var meters = new List<WaterMeter> { _mainMeter };
        SetupMeters(meters);
        SetupEmptyReadings(meters);

        var stream = CreateExcelStream(
            dates: new[] { new DateTime(2026, 1, 15) },
            meterRows: new[]
            {
                new MeterRow("MAIN-001", new object[] { 100m }),
                new MeterRow("UNKNOWN-999", new object[] { 50m })
            });

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Errors.Should().Contain(e => e.Message.Contains("UNKNOWN-999") && e.Message.Contains("neodpovídá"));
    }

    [Fact]
    public async Task ParseAndValidateAsync_NoMetersConfigured_ReturnsError()
    {
        // Arrange
        _meterRepoMock.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Meter))
            .ReturnsAsync(new List<WaterMeter>());

        var stream = CreateExcelStream(
            dates: new[] { new DateTime(2026, 1, 15) },
            meterRows: new[]
            {
                new MeterRow("MAIN-001", new object[] { 100m })
            });

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("nejsou nakonfigurované");
    }

    [Fact]
    public async Task ParseAndValidateAsync_CzechNumberFormat_ParsesCorrectly()
    {
        // Arrange
        var meters = new List<WaterMeter> { _mainMeter };
        SetupMeters(meters);
        SetupEmptyReadings(meters);

        // Create Excel with string value in Czech format
        var stream = CreateExcelWithStringValue(
            date: new DateTime(2026, 1, 15),
            meterNumber: "MAIN-001",
            stringValue: "1 542,7");

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Errors.Should().BeEmpty();
        result.Rows.Should().HaveCount(1);
        result.Rows[0].MeterValues["meter-main"].Should().Be(1542.7m);
    }

    private static ConfirmImportRequest Confirm(params (string meterId, int month, decimal value)[] readings) => new()
    {
        Readings = readings
            .Select(r => new ConfirmImportReading
            {
                MeterId = r.meterId,
                ReadingDate = new DateTime(2026, r.month, 15, 0, 0, 0, DateTimeKind.Utc),
                Value = r.value,
            })
            .ToList(),
    };

    private static MeterReading Stored(string meterId, int month, decimal value) => new()
    {
        MeterId = meterId,
        ReadingDate = new DateTime(2026, month, 15, 0, 0, 0, DateTimeKind.Utc),
        Value = value,
    };

    [Fact]
    public async Task ConfirmImportAsync_ValidReadings_SavesAll()
    {
        var meters = new List<WaterMeter> { _mainMeter, _houseMeter1 };
        SetupMeters(meters);
        SetupEmptyReadings(meters);

        var count = await _useCase.ConfirmImportAsync(Confirm(("meter-main", 1, 100m), ("meter-house1", 1, 30m)), "user-1");

        count.Should().Be(2);
        _readingRepoMock.Verify(r => r.UpsertAsync(It.Is<MeterReading>(m =>
            m.MeterId == "meter-main" && m.Value == 100m && m.Source == ReadingSource.Import && m.ImportedBy == "user-1" &&
            m.ReadingDate == new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc))), Times.Once);
        _readingRepoMock.Verify(r => r.UpsertAsync(It.IsAny<MeterReading>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ConfirmImportAsync_Empty_Throws()
    {
        var act = () => _useCase.ConfirmImportAsync(new ConfirmImportRequest(), "user-1");

        await act.Should().ThrowAsync<Exceptions.AppException>().WithMessage("*Nejsou žádné odečty k importu*");
    }

    [Fact]
    public async Task ConfirmImportAsync_ReadingAddedSincePreview_Returns409AndSavesNothing()
    {
        var meters = new List<WaterMeter> { _mainMeter, _houseMeter1 };
        SetupMeters(meters);
        _readingRepoMock.Setup(r => r.GetByMeterIdAsync("meter-main")).ReturnsAsync(new List<MeterReading>());
        _readingRepoMock.Setup(r => r.GetByMeterIdAsync("meter-house1"))
            .ReturnsAsync(new List<MeterReading> { Stored("meter-house1", 1, 25m) });

        // The first reading is valid, but the batch must not be half-saved.
        var act = () => _useCase.ConfirmImportAsync(Confirm(("meter-main", 1, 100m), ("meter-house1", 1, 30m)), "user-1");

        var ex = await act.Should().ThrowAsync<Exceptions.AppException>();
        ex.Which.StatusCode.Should().Be(409);
        ex.Which.Message.Should().Contain("nic nebylo uloženo");
        _readingRepoMock.Verify(r => r.UpsertAsync(It.IsAny<MeterReading>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmImportAsync_NegativeConsumption_Returns400AndSavesNothing()
    {
        var meters = new List<WaterMeter> { _mainMeter };
        SetupMeters(meters);
        _readingRepoMock.Setup(r => r.GetByMeterIdAsync("meter-main"))
            .ReturnsAsync(new List<MeterReading> { Stored("meter-main", 1, 500m) });

        var act = () => _useCase.ConfirmImportAsync(Confirm(("meter-main", 2, 400m)), "user-1");

        var ex = await act.Should().ThrowAsync<Exceptions.AppException>();
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.Message.Should().Contain("Záporná spotřeba");
        _readingRepoMock.Verify(r => r.UpsertAsync(It.IsAny<MeterReading>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmImportAsync_UnknownMeterOrDuplicateInBatch_Returns400()
    {
        var meters = new List<WaterMeter> { _mainMeter };
        SetupMeters(meters);
        SetupEmptyReadings(meters);

        var unknown = () => _useCase.ConfirmImportAsync(Confirm(("no-such-meter", 1, 1m)), "user-1");
        (await unknown.Should().ThrowAsync<Exceptions.AppException>()).Which.StatusCode.Should().Be(400);

        var duplicate = () => _useCase.ConfirmImportAsync(Confirm(("meter-main", 1, 100m), ("meter-main", 1, 101m)), "user-1");
        (await duplicate.Should().ThrowAsync<Exceptions.AppException>()).Which.Message.Should().Contain("Duplicita");

        _readingRepoMock.Verify(r => r.UpsertAsync(It.IsAny<MeterReading>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmImportAsync_RetryAfterPartialWrite_SkipsAlreadySavedReadings()
    {
        var meters = new List<WaterMeter> { _mainMeter, _houseMeter1 };
        SetupMeters(meters);
        // meter-main was written by the interrupted first attempt (same date + value).
        _readingRepoMock.Setup(r => r.GetByMeterIdAsync("meter-main"))
            .ReturnsAsync(new List<MeterReading> { Stored("meter-main", 1, 100m) });
        _readingRepoMock.Setup(r => r.GetByMeterIdAsync("meter-house1")).ReturnsAsync(new List<MeterReading>());

        var count = await _useCase.ConfirmImportAsync(Confirm(("meter-main", 1, 100m), ("meter-house1", 1, 30m)), "user-1");

        count.Should().Be(1);
        _readingRepoMock.Verify(r => r.UpsertAsync(It.Is<MeterReading>(m => m.MeterId == "meter-house1")), Times.Once);
        _readingRepoMock.Verify(r => r.UpsertAsync(It.Is<MeterReading>(m => m.MeterId == "meter-main")), Times.Never);
    }

    [Fact]
    public async Task ParseAndValidateAsync_MultipleDates_AllProcessed()
    {
        // Arrange
        var meters = new List<WaterMeter> { _mainMeter };
        SetupMeters(meters);
        SetupEmptyReadings(meters);

        var stream = CreateExcelStream(
            dates: new[]
            {
                new DateTime(2026, 1, 15),
                new DateTime(2026, 2, 15),
                new DateTime(2026, 3, 15)
            },
            meterRows: new[]
            {
                new MeterRow("MAIN-001", new object[] { 100m, 110m, 120m })
            });

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Errors.Should().BeEmpty();
        result.Rows.Should().HaveCount(3);
    }

    // ─── Helper types ─────────────────────────────────

    private record MeterRow(string MeterNumber, object[] Values);

    // ─── Helper methods ─────────────────────────────────

    private void SetupMeters(List<WaterMeter> meters)
    {
        _meterRepoMock.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Meter))
            .ReturnsAsync(meters);
    }

    private void SetupEmptyReadings(List<WaterMeter> meters)
    {
        foreach (var meter in meters)
        {
            _readingRepoMock.Setup(r => r.GetByMeterIdAsync(meter.Id))
                .ReturnsAsync(new List<MeterReading>());
        }
    }

    /// <summary>
    /// Creates an Excel stream in the TRANSPOSED format:
    /// Row 1 (header): "Vodoměr" | date1 | date2 | ...
    /// Row 2+: meterNumber | value1 | value2 | ...
    /// </summary>
    private static Stream CreateExcelStream(DateTime[] dates, MeterRow[] meterRows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Odečty");

        // Write header row: A1 = label, B1+ = dates
        worksheet.Cell(1, 1).Value = "Vodoměr";
        for (var col = 0; col < dates.Length; col++)
        {
            worksheet.Cell(1, col + 2).Value = dates[col];
        }

        // Write meter rows (row 2 onwards)
        for (var rowIdx = 0; rowIdx < meterRows.Length; rowIdx++)
        {
            var row = meterRows[rowIdx];
            worksheet.Cell(rowIdx + 2, 1).Value = row.MeterNumber;

            for (var colIdx = 0; colIdx < row.Values.Length; colIdx++)
            {
                var cell = worksheet.Cell(rowIdx + 2, colIdx + 2);
                var value = row.Values[colIdx];

                if (value is decimal decimalValue)
                {
                    cell.Value = (double)decimalValue;
                }
                else
                {
                    cell.Value = value?.ToString() ?? "";
                }
            }
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Creates an Excel in transposed format where one meter row has a missing value.
    /// Row 1: "Vodoměr" | date
    /// Row 2: meterWithValue | mainValue
    /// Row 3: meterWithMissing | (empty)
    /// </summary>
    private static Stream CreateExcelWithMissingValue(DateTime date, string meterWithValue, decimal mainValue, string meterWithMissing)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Odečty");

        // Header row
        worksheet.Cell(1, 1).Value = "Vodoměr";
        worksheet.Cell(1, 2).Value = date;

        // Meter with value
        worksheet.Cell(2, 1).Value = meterWithValue;
        worksheet.Cell(2, 2).Value = (double)mainValue;

        // Meter with missing value (column B left empty intentionally)
        worksheet.Cell(3, 1).Value = meterWithMissing;

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Creates an Excel in transposed format with a string value (Czech number format).
    /// Row 1: "Vodoměr" | date
    /// Row 2: meterNumber | stringValue (as text)
    /// </summary>
    private static Stream CreateExcelWithStringValue(DateTime date, string meterNumber, string stringValue)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Odečty");

        // Header row
        worksheet.Cell(1, 1).Value = "Vodoměr";
        worksheet.Cell(1, 2).Value = date;

        // Meter row with string value
        worksheet.Cell(2, 1).Value = meterNumber;
        worksheet.Cell(2, 2).SetValue(stringValue); // Force string type

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
