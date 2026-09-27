namespace Oaza.Application.DTOs;

// Import preview response
public class ImportPreviewResponse
{
    public List<ImportPreviewRow> Rows { get; set; } = new();
    public List<ImportValidationMessage> Errors { get; set; } = new();
    public List<ImportValidationMessage> Warnings { get; set; } = new();
    public string ImportSessionId { get; set; } = string.Empty;
}

public class ImportPreviewRow
{
    public DateTime ReadingDate { get; set; }
    public Dictionary<string, decimal> MeterValues { get; set; } = new();
}

public class ImportValidationMessage
{
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? Row { get; set; }
    public string? MeterId { get; set; }
}

// Confirm import request
public class ConfirmImportRequest
{
    public string ImportSessionId { get; set; } = string.Empty;
}

// Clipboard import request (paste from meter-reader export)
public class ClipboardImportRequest
{
    public string Text { get; set; } = string.Empty;
    public DateTime ReadingDate { get; set; }
}

// Manual reading entry
public class CreateReadingRequest
{
    public string MeterId { get; set; } = string.Empty;
    public DateTime ReadingDate { get; set; }
    public decimal Value { get; set; }

    /// <summary>The value is an estimate, not a physical reading (T04). Requires <see cref="EstimateNote"/>.</summary>
    public bool IsEstimate { get; set; }
    public string? EstimateNote { get; set; }
}

// Estimate of a meter state at a date (T04)
public class ReadingEstimateResponse
{
    public string MeterId { get; set; } = string.Empty;
    public DateTime TargetDate { get; set; }
    /// <summary>Null when the meter has no readings at all.</summary>
    public decimal? Value { get; set; }
    public bool IsEstimate { get; set; }
    /// <summary>Exact | Interpolated | NearestBefore | NearestAfter | None.</summary>
    public string Method { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

// Update reading
public class UpdateReadingRequest
{
    public decimal Value { get; set; }
    public DateTime? NewDate { get; set; }
}

// Reading response
public class ReadingResponse
{
    public string MeterId { get; set; } = string.Empty;
    public string MeterNumber { get; set; } = string.Empty;
    public string? HouseName { get; set; }
    public DateTime ReadingDate { get; set; }
    public decimal Value { get; set; }
    public decimal? Consumption { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; }
    public string ImportedBy { get; set; } = string.Empty;
    public bool IsEstimate { get; set; }
    public string? EstimateNote { get; set; }
}

// Monthly readings overview
public class MonthlyReadingsResponse
{
    public int Year { get; set; }
    public int Month { get; set; }
    public List<ReadingResponse> Readings { get; set; } = new();
}
