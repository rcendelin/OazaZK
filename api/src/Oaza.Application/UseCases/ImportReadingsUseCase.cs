using System.Globalization;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Interfaces;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.UseCases;

public class ImportReadingsUseCase
{
    private readonly IMeterReadingRepository _readingRepository;
    private readonly IWaterMeterRepository _meterRepository;
    private readonly IImportSessionCache _sessionCache;
    private readonly ILogger<ImportReadingsUseCase> _logger;

    private static readonly CultureInfo CzechCulture = new("cs-CZ");

    public ImportReadingsUseCase(
        IMeterReadingRepository readingRepository,
        IWaterMeterRepository meterRepository,
        IImportSessionCache sessionCache,
        ILogger<ImportReadingsUseCase> logger)
    {
        _readingRepository = readingRepository ?? throw new ArgumentNullException(nameof(readingRepository));
        _meterRepository = meterRepository ?? throw new ArgumentNullException(nameof(meterRepository));
        _sessionCache = sessionCache ?? throw new ArgumentNullException(nameof(sessionCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Parses an Excel file and validates meter readings. Returns a preview with errors and warnings.
    /// Does NOT save anything — the caller must confirm via ConfirmImportAsync.
    /// </summary>
    public async Task<ImportPreviewResponse> ParseAndValidateAsync(Stream excelStream, string importedBy)
    {
        var errors = new List<ImportValidationMessage>();
        var warnings = new List<ImportValidationMessage>();
        var previewRows = new List<ImportPreviewRow>();
        var readings = new List<MeterReading>();

        // Load all configured meters
        var allMeters = await _meterRepository.GetByPartitionKeyAsync(PartitionKeys.Meter);
        if (allMeters.Count == 0)
        {
            errors.Add(new ImportValidationMessage
            {
                Type = "error",
                Message = "No meters configured in the system. Please create meters first."
            });
            return new ImportPreviewResponse
            {
                Rows = previewRows,
                Errors = errors,
                Warnings = warnings,
                ImportSessionId = string.Empty
            };
        }

        // Parse Excel — TRANSPOSED FORMAT:
        // Row 1 = header: A1="Vodoměr", B1=date1, C1=date2, ...
        // Row 2+ = meter rows: A=meter number, B=value for date1, C=value for date2, ...
        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheets.First();

        // 1. Parse dates from header row (columns B onwards)
        var columnDateMap = new Dictionary<int, DateTime>();
        var headerRow = worksheet.Row(1);
        var lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 1;

        for (var col = 2; col <= lastCol; col++)
        {
            var cell = headerRow.Cell(col);
            if (cell.IsEmpty()) continue;

            DateTime date;
            if (cell.DataType == XLDataType.DateTime)
            {
                date = cell.GetDateTime().Date;
            }
            else
            {
                var dateStr = cell.GetString().Trim();
                if (!TryParseCzechDate(dateStr, out date))
                {
                    errors.Add(new ImportValidationMessage
                    {
                        Type = "error",
                        Message = $"Cannot parse date in column {col}: '{dateStr}'."
                    });
                    continue;
                }
            }
            columnDateMap[col] = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        }

        if (columnDateMap.Count == 0)
        {
            errors.Add(new ImportValidationMessage { Type = "error", Message = "No valid dates found in header row." });
            return new ImportPreviewResponse { Rows = previewRows, Errors = errors, Warnings = warnings, ImportSessionId = string.Empty };
        }

        // 2. Build meter lookup
        var meterLookup = allMeters.ToDictionary(m => m.MeterNumber.Trim(), m => m, StringComparer.OrdinalIgnoreCase);

        // Pre-load existing readings
        var existingReadingsByMeter = new Dictionary<string, IReadOnlyList<MeterReading>>();
        foreach (var meter in allMeters)
        {
            existingReadingsByMeter[meter.Id] = await _readingRepository.GetByMeterIdAsync(meter.Id);
        }

        var now = DateTime.UtcNow;
        var seenMeterDates = new HashSet<(string meterId, DateTime date)>();

        // Group readings by date for preview
        var previewByDate = new Dictionary<DateTime, Dictionary<string, decimal>>();

        // 3. Process meter rows (row 2 onwards)
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

        for (var rowNum = 2; rowNum <= lastRow; rowNum++)
        {
            var meterCell = worksheet.Row(rowNum).Cell(1);
            if (meterCell.IsEmpty()) continue;

            var meterNumber = meterCell.GetString().Trim();

            // Match meter
            WaterMeter? meter = null;
            if (meterLookup.TryGetValue(meterNumber, out var directMatch))
            {
                meter = directMatch;
            }
            else
            {
                // Try substring match
                foreach (var m in allMeters)
                {
                    if (meterNumber.Contains(m.MeterNumber, StringComparison.OrdinalIgnoreCase))
                    {
                        meter = m;
                        break;
                    }
                }
            }

            if (meter == null)
            {
                errors.Add(new ImportValidationMessage
                {
                    Type = "error",
                    Message = $"Row {rowNum}: meter number '{meterNumber}' does not match any configured meter.",
                    Row = rowNum
                });
                continue;
            }

            var existingReadings = existingReadingsByMeter[meter.Id];

            // Process each date column for this meter
            foreach (var (col, readingDate) in columnDateMap)
            {
                var cell = worksheet.Row(rowNum).Cell(col);
                if (cell.IsEmpty())
                {
                    warnings.Add(new ImportValidationMessage
                    {
                        Type = "warning",
                        Message = $"Missing value for meter '{meter.MeterNumber}' at {readingDate:d.M.yyyy}.",
                        Row = rowNum,
                        MeterId = meter.Id
                    });
                    continue;
                }

                if (!TryParseCellValue(cell, out var value))
                {
                    errors.Add(new ImportValidationMessage
                    {
                        Type = "error",
                        Message = $"Cannot parse value for meter '{meter.MeterNumber}' at {readingDate:d.M.yyyy}: '{cell.GetString()}'.",
                        Row = rowNum,
                        MeterId = meter.Id
                    });
                    continue;
                }

                // Duplicate check: same meter + same date in DB
                var duplicate = existingReadings.FirstOrDefault(r =>
                    r.ReadingDate.Date == readingDate.Date);
                if (duplicate is not null)
                {
                    errors.Add(new ImportValidationMessage
                    {
                        Type = "error",
                        Message = $"Duplicate reading for meter '{meter.MeterNumber}' on {readingDate:d.M.yyyy}. Existing: {duplicate.Value}.",
                        Row = rowNum,
                        MeterId = meter.Id
                    });
                    continue;
                }

                // Same-file duplicate
                if (!seenMeterDates.Add((meter.Id, readingDate)))
                {
                    errors.Add(new ImportValidationMessage
                    {
                        Type = "error",
                        Message = $"Duplicate in file for meter '{meter.MeterNumber}' on {readingDate:d.M.yyyy}.",
                        Row = rowNum,
                        MeterId = meter.Id
                    });
                    continue;
                }

                // Negative consumption check
                var previousReading = existingReadings
                    .Where(r => r.ReadingDate < readingDate)
                    .OrderByDescending(r => r.ReadingDate)
                    .FirstOrDefault();

                if (previousReading is not null && value < previousReading.Value)
                {
                    errors.Add(new ImportValidationMessage
                    {
                        Type = "error",
                        Message = $"Negative consumption for '{meter.MeterNumber}': {value} < previous {previousReading.Value}.",
                        Row = rowNum,
                        MeterId = meter.Id
                    });
                    continue;
                }

                // Anomaly detection
                if (previousReading is not null)
                {
                    var consumption = value - previousReading.Value;
                    var recentReadings = existingReadings
                        .OrderByDescending(r => r.ReadingDate)
                        .Take(7).OrderBy(r => r.ReadingDate).ToList();

                    if (recentReadings.Count >= 2)
                    {
                        var deltas = new List<decimal>();
                        for (var i = 1; i < recentReadings.Count; i++)
                        {
                            var d = recentReadings[i].Value - recentReadings[i - 1].Value;
                            if (d > 0) deltas.Add(d);
                        }
                        if (deltas.Count > 0)
                        {
                            var avg = deltas.Average();
                            if (avg > 0 && consumption > 2 * avg)
                            {
                                warnings.Add(new ImportValidationMessage
                                {
                                    Type = "warning",
                                    Message = $"Anomaly for '{meter.MeterNumber}' on {readingDate:d.M.yyyy}: {consumption:F1} m³ > 2× avg ({avg:F1} m³).",
                                    Row = rowNum,
                                    MeterId = meter.Id
                                });
                            }
                        }
                    }
                }

                // Add reading
                readings.Add(new MeterReading
                {
                    MeterId = meter.Id,
                    ReadingDate = readingDate,
                    Value = value,
                    Source = ReadingSource.Import,
                    ImportedAt = now,
                    ImportedBy = importedBy
                });

                if (!previewByDate.ContainsKey(readingDate))
                    previewByDate[readingDate] = new Dictionary<string, decimal>();
                previewByDate[readingDate][meter.Id] = value;
            }
        }

        // Build preview rows from grouped data
        foreach (var (date, values) in previewByDate.OrderBy(kv => kv.Key))
        {
            if (values.Count > 0)
            {
                previewRows.Add(new ImportPreviewRow
                {
                    ReadingDate = date,
                    MeterValues = values
                });
            }
        }

        // Check for meters in the system that have no readings in the import
        var importedMeterIds = readings.Select(r => r.MeterId).ToHashSet();
        var unmappedMeters = allMeters
            .Where(m => !importedMeterIds.Contains(m.Id))
            .ToList();

        foreach (var meter in unmappedMeters)
        {
            warnings.Add(new ImportValidationMessage
            {
                Type = "warning",
                Message = $"Meter '{meter.MeterNumber}' ({meter.Type}) is not mapped to any column in the Excel file."
            });
        }

        // Store session
        var sessionId = Guid.NewGuid().ToString();
        _sessionCache.Store(sessionId, new ImportSessionData
        {
            Readings = readings,
            Errors = errors,
            Warnings = warnings,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = importedBy
        });

        _logger.LogInformation(
            "Import preview generated: {ReadingCount} readings, {ErrorCount} errors, {WarningCount} warnings. Session: {SessionId}.",
            readings.Count, errors.Count, warnings.Count, sessionId);

        return new ImportPreviewResponse
        {
            Rows = previewRows,
            Errors = errors,
            Warnings = warnings,
            ImportSessionId = sessionId
        };
    }

    /// <summary>
    /// Confirms a previously parsed import. Saves all readings to Table Storage.
    /// </summary>
    public async Task<int> ConfirmImportAsync(string importSessionId, string importedBy)
    {
        var session = _sessionCache.Retrieve(importSessionId);
        if (session is null)
        {
            throw new AppException("Import session not found or expired. Please re-upload the file.", 404);
        }

        // Verify the confirming user is the same as the one who created the session
        if (!string.IsNullOrEmpty(session.CreatedBy) && session.CreatedBy != importedBy)
        {
            throw new AppException("You can only confirm your own import sessions.", 403);
        }

        if (session.Errors.Count > 0)
        {
            throw new AppException(
                $"Cannot confirm import with {session.Errors.Count} validation error(s). Please fix the errors and re-upload.");
        }

        if (session.Readings.Count == 0)
        {
            throw new AppException("No readings to import.");
        }

        foreach (var reading in session.Readings)
        {
            // Re-validate: check if a reading already exists in the database for the same meter + month
            var existing = await _readingRepository.GetByMeterIdAsync(reading.MeterId);
            var duplicate = existing.Any(r =>
                r.ReadingDate.Year == reading.ReadingDate.Year &&
                r.ReadingDate.Month == reading.ReadingDate.Month);
            if (duplicate)
            {
                throw new AppException(
                    $"A reading for meter {reading.MeterId} already exists for {reading.ReadingDate:yyyy-MM}. The data may have changed since preview.", 409);
            }

            await _readingRepository.UpsertAsync(reading);
        }

        var count = session.Readings.Count;

        _sessionCache.Remove(importSessionId);

        _logger.LogInformation(
            "Import confirmed: {Count} readings saved by {ImportedBy}. Session: {SessionId}.",
            count, importedBy, importSessionId);

        return count;
    }

    // MapColumnsToMeters removed — transposed format uses rows for meters, not columns.

    private static bool TryParseCellValue(IXLCell cell, out decimal value)
    {
        value = 0;

        if (cell.DataType == XLDataType.Number)
        {
            value = (decimal)cell.GetDouble();
            return true;
        }

        var text = cell.GetString().Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // Remove spaces (Czech format may have space as thousands separator: "1 542,7")
        text = text.Replace(" ", "");

        // Try Czech format first (comma as decimal separator)
        if (decimal.TryParse(text, NumberStyles.Number, CzechCulture, out value))
        {
            return true;
        }

        // Try invariant format as fallback
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseCzechDate(string dateStr, out DateTime date)
    {
        date = default;

        // Try common Czech date formats
        string[] formats = { "d.M.yyyy", "dd.MM.yyyy", "d. M. yyyy", "dd. MM. yyyy", "yyyy-MM-dd" };

        if (DateTime.TryParseExact(dateStr, formats, CzechCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        // Fallback to general parse
        if (DateTime.TryParse(dateStr, CzechCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        return false;
    }
}
