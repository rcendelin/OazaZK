using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Oaza.Application.DTOs;
using Oaza.Application.Interfaces;
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
    private readonly Mock<IImportSessionCache> _cacheMock;
    private readonly Mock<ILogger<ImportReadingsUseCase>> _loggerMock;
    private readonly ImportReadingsUseCase _useCase;

    private readonly WaterMeter _mainMeter;
    private readonly WaterMeter _houseMeter1;
    private readonly WaterMeter _houseMeter2;

    public ImportReadingsUseCaseTests()
    {
        _readingRepoMock = new Mock<IMeterReadingRepository>();
        _meterRepoMock = new Mock<IWaterMeterRepository>();
        _cacheMock = new Mock<IImportSessionCache>();
        _loggerMock = new Mock<ILogger<ImportReadingsUseCase>>();

        _useCase = new ImportReadingsUseCase(
            _readingRepoMock.Object,
            _meterRepoMock.Object,
            _cacheMock.Object,
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
            headers: new[] { "Datum", "MAIN-001", "IND-001", "IND-002" },
            rows: new[]
            {
                new object[] { new DateTime(2026, 1, 15), 100.5m, 30.2m, 25.1m }
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
        result.ImportSessionId.Should().NotBeNullOrEmpty();

        _cacheMock.Verify(c => c.Store(It.IsAny<string>(), It.IsAny<ImportSessionData>()), Times.Once);
    }

    [Fact]
    public async Task ParseAndValidateAsync_DuplicateReading_ReturnsError()
    {
        // Arrange
        var meters = new List<WaterMeter> { _mainMeter };
        SetupMeters(meters);

        // Existing reading for January 2026
        _readingRepoMock.Setup(r => r.GetByMeterIdAsync("meter-main"))
            .ReturnsAsync(new List<MeterReading>
            {
                new()
                {
                    MeterId = "meter-main",
                    ReadingDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                    Value = 90m,
                    Source = ReadingSource.Manual,
                    ImportedAt = DateTime.UtcNow,
                    ImportedBy = "user-1"
                }
            });

        var stream = CreateExcelStream(
            headers: new[] { "Datum", "MAIN-001" },
            rows: new[]
            {
                new object[] { new DateTime(2026, 1, 15), 100m }
            });

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Type.Should().Be("error");
        result.Errors[0].Message.Should().Contain("Duplicate reading");
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
            headers: new[] { "Datum", "MAIN-001" },
            rows: new[]
            {
                new object[] { new DateTime(2026, 1, 15), 90m }
            });

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("Negative consumption");
    }

    [Fact]
    public async Task ParseAndValidateAsync_MissingMeterValue_ReturnsWarning()
    {
        // Arrange
        var meters = new List<WaterMeter> { _mainMeter, _houseMeter1 };
        SetupMeters(meters);
        SetupEmptyReadings(meters);

        // Excel has columns for both meters but value for houseMeter1 is empty
        var stream = CreateExcelWithMissingValue(
            headers: new[] { "Datum", "MAIN-001", "IND-001" },
            date: new DateTime(2026, 1, 15),
            mainValue: 100m);

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Warnings.Should().Contain(w => w.Message.Contains("Missing value"));
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
            headers: new[] { "Datum", "MAIN-001" },
            rows: new[]
            {
                new object[] { new DateTime(2026, 2, 15), 200m }
            });

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Warnings.Should().Contain(w => w.Message.Contains("Anomaly detected"));
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAndValidateAsync_UnmatchedHeader_ReturnsError()
    {
        // Arrange
        var meters = new List<WaterMeter> { _mainMeter };
        SetupMeters(meters);
        SetupEmptyReadings(meters);

        var stream = CreateExcelStream(
            headers: new[] { "Datum", "MAIN-001", "UNKNOWN-999" },
            rows: new[]
            {
                new object[] { new DateTime(2026, 1, 15), 100m, 50m }
            });

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Errors.Should().Contain(e => e.Message.Contains("UNKNOWN-999") && e.Message.Contains("does not match"));
    }

    [Fact]
    public async Task ParseAndValidateAsync_NoMetersConfigured_ReturnsError()
    {
        // Arrange
        _meterRepoMock.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Meter))
            .ReturnsAsync(new List<WaterMeter>());

        var stream = CreateExcelStream(
            headers: new[] { "Datum", "MAIN-001" },
            rows: new[] { new object[] { new DateTime(2026, 1, 15), 100m } });

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Message.Should().Contain("No meters configured");
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
            headers: new[] { "Datum", "MAIN-001" },
            date: new DateTime(2026, 1, 15),
            stringValue: "1 542,7");

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Errors.Should().BeEmpty();
        result.Rows.Should().HaveCount(1);
        result.Rows[0].MeterValues["meter-main"].Should().Be(1542.7m);
    }

    [Fact]
    public async Task ConfirmImportAsync_ValidSession_SavesReadings()
    {
        // Arrange
        var sessionId = "test-session-123";
        var readings = new List<MeterReading>
        {
            new()
            {
                MeterId = "meter-main",
                ReadingDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                Value = 100m,
                Source = ReadingSource.Import,
                ImportedAt = DateTime.UtcNow,
                ImportedBy = "user-1"
            },
            new()
            {
                MeterId = "meter-house1",
                ReadingDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                Value = 30m,
                Source = ReadingSource.Import,
                ImportedAt = DateTime.UtcNow,
                ImportedBy = "user-1"
            }
        };

        _cacheMock.Setup(c => c.Retrieve(sessionId))
            .Returns(new ImportSessionData
            {
                Readings = readings,
                Errors = new List<ImportValidationMessage>(),
                Warnings = new List<ImportValidationMessage>(),
                CreatedAt = DateTime.UtcNow
            });

        // Setup GetByMeterIdAsync to return empty lists (no existing readings)
        _readingRepoMock.Setup(r => r.GetByMeterIdAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<MeterReading>().AsReadOnly());

        // Act
        var count = await _useCase.ConfirmImportAsync(sessionId, "user-1");

        // Assert
        count.Should().Be(2);
        _readingRepoMock.Verify(r => r.UpsertAsync(It.IsAny<MeterReading>()), Times.Exactly(2));
        _cacheMock.Verify(c => c.Remove(sessionId), Times.Once);
    }

    [Fact]
    public async Task ConfirmImportAsync_SessionNotFound_ThrowsAppException()
    {
        // Arrange
        _cacheMock.Setup(c => c.Retrieve("nonexistent")).Returns((ImportSessionData?)null);

        // Act & Assert
        var act = () => _useCase.ConfirmImportAsync("nonexistent", "user-1");
        await act.Should().ThrowAsync<Exceptions.AppException>()
            .WithMessage("*not found or expired*");
    }

    [Fact]
    public async Task ConfirmImportAsync_SessionWithErrors_ThrowsAppException()
    {
        // Arrange
        var sessionId = "session-with-errors";
        _cacheMock.Setup(c => c.Retrieve(sessionId))
            .Returns(new ImportSessionData
            {
                Readings = new List<MeterReading>
                {
                    new() { MeterId = "m1", Value = 100m }
                },
                Errors = new List<ImportValidationMessage>
                {
                    new() { Type = "error", Message = "Some error" }
                },
                Warnings = new List<ImportValidationMessage>(),
                CreatedAt = DateTime.UtcNow
            });

        // Act & Assert
        var act = () => _useCase.ConfirmImportAsync(sessionId, "user-1");
        await act.Should().ThrowAsync<Exceptions.AppException>()
            .WithMessage("*validation error*");
    }

    [Fact]
    public async Task ConfirmImportAsync_EmptyReadings_ThrowsAppException()
    {
        // Arrange
        var sessionId = "session-empty";
        _cacheMock.Setup(c => c.Retrieve(sessionId))
            .Returns(new ImportSessionData
            {
                Readings = new List<MeterReading>(),
                Errors = new List<ImportValidationMessage>(),
                Warnings = new List<ImportValidationMessage>(),
                CreatedAt = DateTime.UtcNow
            });

        // Act & Assert
        var act = () => _useCase.ConfirmImportAsync(sessionId, "user-1");
        await act.Should().ThrowAsync<Exceptions.AppException>()
            .WithMessage("*No readings to import*");
    }

    [Fact]
    public async Task ParseAndValidateAsync_MultipleRows_AllProcessed()
    {
        // Arrange
        var meters = new List<WaterMeter> { _mainMeter };
        SetupMeters(meters);
        SetupEmptyReadings(meters);

        var stream = CreateExcelStream(
            headers: new[] { "Datum", "MAIN-001" },
            rows: new[]
            {
                new object[] { new DateTime(2026, 1, 15), 100m },
                new object[] { new DateTime(2026, 2, 15), 110m },
                new object[] { new DateTime(2026, 3, 15), 120m }
            });

        // Act
        var result = await _useCase.ParseAndValidateAsync(stream, "user-1");

        // Assert
        result.Errors.Should().BeEmpty();
        result.Rows.Should().HaveCount(3);
    }

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

    private static Stream CreateExcelStream(string[] headers, object[][] rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Odečty");

        // Write headers
        for (var col = 0; col < headers.Length; col++)
        {
            worksheet.Cell(1, col + 1).Value = headers[col];
        }

        // Write rows
        for (var rowIdx = 0; rowIdx < rows.Length; rowIdx++)
        {
            for (var colIdx = 0; colIdx < rows[rowIdx].Length; colIdx++)
            {
                var cell = worksheet.Cell(rowIdx + 2, colIdx + 1);
                var value = rows[rowIdx][colIdx];

                if (value is DateTime dateValue)
                {
                    cell.Value = dateValue;
                }
                else if (value is decimal decimalValue)
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

    private static Stream CreateExcelWithMissingValue(string[] headers, DateTime date, decimal mainValue)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Odečty");

        for (var col = 0; col < headers.Length; col++)
        {
            worksheet.Cell(1, col + 1).Value = headers[col];
        }

        worksheet.Cell(2, 1).Value = date;
        worksheet.Cell(2, 2).Value = (double)mainValue;
        // Column 3 (IND-001) is left empty intentionally

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static Stream CreateExcelWithStringValue(string[] headers, DateTime date, string stringValue)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Odečty");

        for (var col = 0; col < headers.Length; col++)
        {
            worksheet.Cell(1, col + 1).Value = headers[col];
        }

        worksheet.Cell(2, 1).Value = date;
        worksheet.Cell(2, 2).SetValue(stringValue); // Force string type

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
