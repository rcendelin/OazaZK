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

        // Parse Excel
        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheets.First();

        // Read header row to map columns to meters
        var columnMeterMap = MapColumnsToMeters(worksheet, allMeters, errors);

        if (columnMeterMap.Count == 0 && errors.Count > 0)
        {
            return new ImportPreviewResponse
            {
                Rows = previewRows,
                Errors = errors,
                Warnings = warnings,
                ImportSessionId = string.Empty
            };
        }

        // Pre-load existing readings for all meters (for duplicate and consumption checks)
        var existingReadingsByMeter = new Dictionary<string, IReadOnlyList<MeterReading>>();
        foreach (var meter in allMeters)
        {
            existingReadingsByMeter[meter.Id] = await _readingRepository.GetByMeterIdAsync(meter.Id);
        }

        // Process data rows (starting from row 2, after header)
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        var now = DateTime.UtcNow;
        var seenMeterMonths = new HashSet<(string meterId, int year, int month)>();

        for (var rowNum = 2; rowNum <= lastRow; rowNum++)
        {
            var row = worksheet.Row(rowNum);

            // Column A = date
            var dateCell = row.Cell(1);
            if (dateCell.IsEmpty())
            {
                continue; // skip empty rows
            }

            DateTime readingDate;
            if (dateCell.DataType == XLDataType.DateTime)
            {
                readingDate = dateCell.GetDateTime().Date;
            }
            else
            {
                var dateStr = dateCell.GetString().Trim();
                if (!TryParseCzechDate(dateStr, out readingDate))
                {
                    errors.Add(new ImportValidationMessage
                    {
                        Type = "error",
                        Message = $"Cannot parse date: '{dateStr}'.",
                        Row = rowNum
                    });
                    continue;
                }
            }

            readingDate = DateTime.SpecifyKind(readingDate.Date, DateTimeKind.Utc);

            var meterValues = new Dictionary<string, decimal>();

            foreach (var (colIndex, meter) in columnMeterMap)
            {
                var cell = row.Cell(colIndex);
                if (cell.IsEmpty())
                {
                    warnings.Add(new ImportValidationMessage
                    {
                        Type = "warning",
                        Message = $"Missing value for meter '{meter.MeterNumber}'.",
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
                        Message = $"Cannot parse value for meter '{meter.MeterNumber}': '{cell.GetString()}'.",
                        Row = rowNum,
                        MeterId = meter.Id
                    });
                    continue;
                }

                // Duplicate check: reading already exists for same meter + same month
                var existingReadings = existingReadingsByMeter[meter.Id];
                var duplicate = existingReadings.FirstOrDefault(r =>
                    r.ReadingDate.Year == readingDate.Year && r.ReadingDate.Month == readingDate.Month);
                if (duplicate is not null)
                {
                    errors.Add(new ImportValidationMessage
                    {
                        Type = "error",
                        Message = $"Duplicate reading for meter '{meter.MeterNumber}' in {readingDate:yyyy-MM}. Existing value: {duplicate.Value}.",
                        Row = rowNum,
                        MeterId = meter.Id
                    });
                    continue;
                }

                // Same-file duplicate check
                var meterMonthKey = (meter.Id, readingDate.Year, readingDate.Month);
                if (!seenMeterMonths.Add(meterMonthKey))
                {
                    errors.Add(new ImportValidationMessage
                    {
                        Type = "error",
                        Message = $"Duplicate reading within the file for meter '{meter.MeterNumber}' in {readingDate:yyyy-MM}.",
                        Row = rowNum,
                        MeterId = meter.Id
                    });
                    continue;
                }

                // Negative consumption check: new value < previous reading value
                var previousReading = existingReadings
                    .Where(r => r.ReadingDate < readingDate)
                    .OrderByDescending(r => r.ReadingDate)
                    .FirstOrDefault();

                if (previousReading is not null && value < previousReading.Value)
                {
                    errors.Add(new ImportValidationMessage
                    {
                        Type = "error",
                        Message = $"Negative consumption for meter '{meter.MeterNumber}': new value {value} < previous value {previousReading.Value}.",
                        Row = rowNum,
                        MeterId = meter.Id
                    });
                    continue;
                }

                // Anomaly detection: consumption > 2x rolling average of last 6 months
                if (previousReading is not null)
                {
                    var consumption = value - previousReading.Value;
                    var recentReadings = existingReadings
                        .OrderByDescending(r => r.ReadingDate)
                        .Take(7) // take 7 to compute 6 consumption deltas
                        .OrderBy(r => r.ReadingDate)
                        .ToList();

                    if (recentReadings.Count >= 2)
                    {
                        var recentConsumptions = new List<decimal>();
                        for (var i = 1; i < recentReadings.Count; i++)
                        {
                            var delta = recentReadings[i].Value - recentReadings[i - 1].Value;
                            if (delta > 0)
                            {
                                recentConsumptions.Add(delta);
                            }
                        }

                        if (recentConsumptions.Count > 0)
                        {
                            var average = recentConsumptions.Average();
                            if (average > 0 && consumption > 2 * average)
                            {
                                warnings.Add(new ImportValidationMessage
                                {
                                    Type = "warning",
                                    Message = $"Anomaly detected for meter '{meter.MeterNumber}': consumption {consumption} m³ is more than 2× the rolling average ({average:F1} m³).",
                                    Row = rowNum,
                                    MeterId = meter.Id
                                });
                            }
                        }
                    }
                }

                meterValues[meter.Id] = value;

                readings.Add(new MeterReading
                {
                    MeterId = meter.Id,
                    ReadingDate = readingDate,
                    Value = value,
                    Source = ReadingSource.Import,
                    ImportedAt = now,
                    ImportedBy = importedBy
                });
            }

            // Check completeness: all configured meters must have a value
            var missingMeters = allMeters
                .Where(m => columnMeterMap.Values.Any(cm => cm.Id == m.Id) && !meterValues.ContainsKey(m.Id))
                .ToList();

            // Note: missing values already warned per-cell above

            if (meterValues.Count > 0)
            {
                previewRows.Add(new ImportPreviewRow
                {
                    ReadingDate = readingDate,
                    MeterValues = meterValues
                });
            }
        }

        // Check for meters in the system that are not mapped to any column
        var unmappedMeters = allMeters
            .Where(m => !columnMeterMap.Values.Any(cm => cm.Id == m.Id))
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

    /// <summary>
    /// Maps Excel header columns to configured water meters by matching meter numbers.
    /// Column A is reserved for dates. Columns B onwards are matched to meters.
    /// </summary>
    private static Dictionary<int, WaterMeter> MapColumnsToMeters(
        IXLWorksheet worksheet, IReadOnlyList<WaterMeter> meters, List<ImportValidationMessage> errors)
    {
        var map = new Dictionary<int, WaterMeter>();
        var headerRow = worksheet.Row(1);
        var lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 1;

        // Build a lookup by meter number (case-insensitive, trimmed)
        var meterLookup = meters.ToDictionary(
            m => m.MeterNumber.Trim(),
            m => m,
            StringComparer.OrdinalIgnoreCase);

        for (var col = 2; col <= lastCol; col++)
        {
            var headerCell = headerRow.Cell(col);
            if (headerCell.IsEmpty())
            {
                continue;
            }

            var headerValue = headerCell.GetString().Trim();

            // Try to match by meter number directly
            if (meterLookup.TryGetValue(headerValue, out var meter))
            {
                map[col] = meter;
                continue;
            }

            // Try to find meter number embedded in the header text
            var matched = false;
            foreach (var m in meters)
            {
                if (headerValue.Contains(m.MeterNumber, StringComparison.OrdinalIgnoreCase))
                {
                    map[col] = m;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                errors.Add(new ImportValidationMessage
                {
                    Type = "error",
                    Message = $"Column {col} header '{headerValue}' does not match any configured meter number."
                });
            }
        }

        // Check for duplicate meters (same meter mapped to multiple columns)
        var duplicateMeterIds = map.Values
            .GroupBy(m => m.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var duplicateId in duplicateMeterIds)
        {
            var meter = map.Values.First(m => m.Id == duplicateId);
            var columns = map.Where(kv => kv.Value.Id == duplicateId).Select(kv => kv.Key);
            errors.Add(new ImportValidationMessage
            {
                Type = "error",
                Message = $"Meter '{meter.MeterNumber}' is mapped to multiple columns: {string.Join(", ", columns)}."
            });
        }

        return map;
    }

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
