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
                Message = "V systému nejsou nakonfigurované žádné vodoměry. Nejprve prosím vytvořte vodoměry."
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
                        Message = $"Nelze zpracovat datum ve sloupci {col}: '{dateStr}'."
                    });
                    continue;
                }
            }
            columnDateMap[col] = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        }

        if (columnDateMap.Count == 0)
        {
            errors.Add(new ImportValidationMessage { Type = "error", Message = "V hlavičkovém řádku nebylo nalezeno žádné platné datum." });
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
                    Message = $"Řádek {rowNum}: číslo vodoměru '{meterNumber}' neodpovídá žádnému nakonfigurovanému vodoměru.",
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
                        Message = $"Chybí hodnota pro vodoměr '{meter.MeterNumber}' k datu {readingDate:d.M.yyyy}.",
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
                        Message = $"Nelze zpracovat hodnotu pro vodoměr '{meter.MeterNumber}' k datu {readingDate:d.M.yyyy}: '{cell.GetString()}'.",
                        Row = rowNum,
                        MeterId = meter.Id
                    });
                    continue;
                }

                // Validate (duplicate / negative / anomaly) — shared with the clipboard import.
                if (!TryValidateReading(meter, readingDate, value, existingReadings,
                        seenMeterDates, rowNum, errors, warnings))
                {
                    continue;
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
                Message = $"Vodoměr '{meter.MeterNumber}' ({meter.Type}) není namapován na žádný sloupec v Excel souboru."
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
            throw new AppException("Importní relace nebyla nalezena nebo vypršela. Nahrajte prosím soubor znovu.", 404);
        }

        // Verify the confirming user is the same as the one who created the session
        if (!string.IsNullOrEmpty(session.CreatedBy) && session.CreatedBy != importedBy)
        {
            throw new AppException("Můžete potvrdit pouze vlastní importní relace.", 403);
        }

        if (session.Errors.Count > 0)
        {
            throw new AppException(
                $"Nelze potvrdit import s {session.Errors.Count} chybami validace. Opravte prosím chyby a nahrajte soubor znovu.");
        }

        if (session.Readings.Count == 0)
        {
            throw new AppException("Nejsou žádné odečty k importu.");
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
                    $"Odečet pro vodoměr {reading.MeterId} za {reading.ReadingDate:yyyy-MM} již existuje. Data se od náhledu mohla změnit.", 409);
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
    /// Parses a tab-separated meter-reader clipboard export and validates the readings
    /// for a single reading date chosen by the admin. Meters are matched by their
    /// RadioAddress (the "Address" column); the reading is the "Value 1" column (m³).
    /// Does NOT save anything — confirm via ConfirmImportAsync.
    /// </summary>
    public async Task<ImportPreviewResponse> ParseClipboardAndValidateAsync(
        string pastedText, DateTime readingDate, string importedBy)
    {
        var errors = new List<ImportValidationMessage>();
        var warnings = new List<ImportValidationMessage>();
        var previewRows = new List<ImportPreviewRow>();
        var readings = new List<MeterReading>();

        ImportPreviewResponse Result() => new()
        {
            Rows = previewRows,
            Errors = errors,
            Warnings = warnings,
            ImportSessionId = string.Empty
        };

        if (string.IsNullOrWhiteSpace(pastedText))
        {
            errors.Add(new ImportValidationMessage { Type = "error", Message = "Vložený text je prázdný." });
            return Result();
        }

        readingDate = DateTime.SpecifyKind(readingDate.Date, DateTimeKind.Utc);

        var allMeters = await _meterRepository.GetByPartitionKeyAsync(PartitionKeys.Meter);
        if (allMeters.Count == 0)
        {
            errors.Add(new ImportValidationMessage { Type = "error", Message = "V systému nejsou nakonfigurované žádné vodoměry. Nejprve prosím vytvořte vodoměry." });
            return Result();
        }

        // Meter lookup by physical RadioAddress.
        var meterByAddress = allMeters
            .Where(m => !string.IsNullOrWhiteSpace(m.RadioAddress))
            .GroupBy(m => m.RadioAddress!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Fallback lookup by identifier (MeterNumber) — admins often put the physical
        // address straight into the meter's identifier instead of the Address field.
        var meterByNumber = allMeters
            .Where(m => !string.IsNullOrWhiteSpace(m.MeterNumber))
            .GroupBy(m => m.MeterNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var lines = pastedText.Replace("\r\n", "\n").Replace("\r", "\n")
            .Split('\n')
            .Where(l => l.Trim().Length > 0)
            .ToList();

        if (lines.Count < 2)
        {
            errors.Add(new ImportValidationMessage { Type = "error", Message = "Vložte hlavičku a alespoň jeden řádek s odečtem (odděleno tabulátory)." });
            return Result();
        }

        // Header → column indices (tab-separated)
        var header = lines[0].Split('\t').Select(h => h.Trim()).ToList();
        int IndexOf(string name) => header.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));

        var addressCol = IndexOf("Address");
        if (addressCol < 0)
        {
            errors.Add(new ImportValidationMessage { Type = "error", Message = "V hlavičce chybí sloupec 'Address'. Zkopírujte data včetně řádku s názvy sloupců (odděleno tabulátory)." });
            return Result();
        }

        var valueCol = IndexOf("Value 1");
        if (valueCol < 0) valueCol = IndexOf("Value");
        if (valueCol < 0)
        {
            errors.Add(new ImportValidationMessage { Type = "error", Message = "V hlavičce chybí sloupec 'Value 1' (odečet v m³)." });
            return Result();
        }

        // Pre-load existing readings
        var existingReadingsByMeter = new Dictionary<string, IReadOnlyList<MeterReading>>();
        foreach (var meter in allMeters)
        {
            existingReadingsByMeter[meter.Id] = await _readingRepository.GetByMeterIdAsync(meter.Id);
        }

        var now = DateTime.UtcNow;
        var seenMeterDates = new HashSet<(string meterId, DateTime date)>();
        var values = new Dictionary<string, decimal>();

        for (var i = 1; i < lines.Count; i++)
        {
            var rowNum = i + 1;
            var cells = lines[i].Split('\t');
            if (cells.Length <= Math.Max(addressCol, valueCol)) continue;

            var address = cells[addressCol].Trim();
            if (string.IsNullOrEmpty(address)) continue;

            // Match by Address (RadioAddress) first, then fall back to the identifier.
            if (!meterByAddress.TryGetValue(address, out var meter)
                && !meterByNumber.TryGetValue(address, out meter))
            {
                errors.Add(new ImportValidationMessage
                {
                    Type = "error",
                    Message = $"Adresa '{address}' neodpovídá žádnému vodoměru (ani podle pole Adresa, ani podle Identifikátoru). Doplňte ji v Admin → Vodoměry.",
                    Row = rowNum
                });
                continue;
            }

            if (!TryParseDecimal(cells[valueCol], out var value))
            {
                errors.Add(new ImportValidationMessage
                {
                    Type = "error",
                    Message = $"Nelze přečíst odečet pro adresu '{address}': '{cells[valueCol].Trim()}'.",
                    Row = rowNum,
                    MeterId = meter.Id
                });
                continue;
            }

            if (!TryValidateReading(meter, readingDate, value, existingReadingsByMeter[meter.Id],
                    seenMeterDates, rowNum, errors, warnings))
            {
                continue;
            }

            readings.Add(new MeterReading
            {
                MeterId = meter.Id,
                ReadingDate = readingDate,
                Value = value,
                Source = ReadingSource.Import,
                ImportedAt = now,
                ImportedBy = importedBy
            });
            values[meter.Id] = value;
        }

        if (values.Count > 0)
        {
            previewRows.Add(new ImportPreviewRow { ReadingDate = readingDate, MeterValues = values });
        }

        // Warn about meters with an address that were not in the pasted text
        var importedMeterIds = readings.Select(r => r.MeterId).ToHashSet();
        foreach (var meter in allMeters.Where(m => !string.IsNullOrWhiteSpace(m.RadioAddress) && !importedMeterIds.Contains(m.Id)))
        {
            warnings.Add(new ImportValidationMessage
            {
                Type = "warning",
                Message = $"Vodoměr '{meter.MeterNumber}' (adresa {meter.RadioAddress}) nebyl ve vloženém textu."
            });
        }

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
            "Clipboard import preview: {ReadingCount} readings, {ErrorCount} errors, {WarningCount} warnings. Session: {SessionId}.",
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
    /// Validates a single reading (duplicate per month, negative consumption, anomaly).
    /// Shared by the Excel and clipboard imports. Appends messages and returns false if
    /// the reading must be skipped.
    /// </summary>
    private static bool TryValidateReading(
        WaterMeter meter,
        DateTime readingDate,
        decimal value,
        IReadOnlyList<MeterReading> existingReadings,
        HashSet<(string meterId, DateTime date)> seenMeterDates,
        int? rowNum,
        List<ImportValidationMessage> errors,
        List<ImportValidationMessage> warnings)
    {
        // Duplicate check: same meter + same MONTH in DB (one reading per meter per month).
        var duplicate = existingReadings.FirstOrDefault(r =>
            r.ReadingDate.Year == readingDate.Year && r.ReadingDate.Month == readingDate.Month);
        if (duplicate is not null)
        {
            errors.Add(new ImportValidationMessage
            {
                Type = "error",
                Message = $"Odečet pro vodoměr '{meter.MeterNumber}' za {readingDate:MM/yyyy} již existuje (ze dne {duplicate.ReadingDate:d.M.yyyy}, hodnota {duplicate.Value}).",
                Row = rowNum,
                MeterId = meter.Id
            });
            return false;
        }

        // Same-batch duplicate: same meter + same month within the import.
        if (!seenMeterDates.Add((meter.Id, new DateTime(readingDate.Year, readingDate.Month, 1))))
        {
            errors.Add(new ImportValidationMessage
            {
                Type = "error",
                Message = $"Duplicita v souboru pro vodoměr '{meter.MeterNumber}' za {readingDate:MM/yyyy}.",
                Row = rowNum,
                MeterId = meter.Id
            });
            return false;
        }

        // Negative consumption check.
        var previousReading = existingReadings
            .Where(r => r.ReadingDate < readingDate)
            .OrderByDescending(r => r.ReadingDate)
            .FirstOrDefault();

        if (previousReading is not null && value < previousReading.Value)
        {
            errors.Add(new ImportValidationMessage
            {
                Type = "error",
                Message = $"Záporná spotřeba pro '{meter.MeterNumber}': {value} < předchozí {previousReading.Value}.",
                Row = rowNum,
                MeterId = meter.Id
            });
            return false;
        }

        // Anomaly detection (warning only).
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
                            Message = $"Anomálie u '{meter.MeterNumber}' k datu {readingDate:d.M.yyyy}: {consumption:F1} m³ > 2× průměr ({avg:F1} m³).",
                            Row = rowNum,
                            MeterId = meter.Id
                        });
                    }
                }
            }
        }

        return true;
    }

    private static bool TryParseCellValue(IXLCell cell, out decimal value)
    {
        value = 0;

        if (cell.DataType == XLDataType.Number)
        {
            value = (decimal)cell.GetDouble();
            return true;
        }

        return TryParseDecimal(cell.GetString(), out value);
    }

    private static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // Remove spaces (Czech format may have space as thousands separator: "1 542,7")
        text = text.Replace(" ", "");

        // Try Czech format first (comma as decimal separator), then invariant.
        return decimal.TryParse(text, NumberStyles.Number, CzechCulture, out value)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
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
