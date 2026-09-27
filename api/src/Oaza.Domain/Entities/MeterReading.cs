using Oaza.Domain.Enums;

namespace Oaza.Domain.Entities;

public class MeterReading
{
    public string MeterId { get; set; } = string.Empty;
    public DateTime ReadingDate { get; set; }
    public decimal Value { get; set; }
    public ReadingSource Source { get; set; }
    public DateTime ImportedAt { get; set; }
    public string ImportedBy { get; set; } = string.Empty;

    /// <summary>
    /// True when the value is not a physical reading but an estimate (e.g. an
    /// interpolated opening reading for a new owner). Shown with a mark in the UI.
    /// </summary>
    public bool IsEstimate { get; set; }

    /// <summary>How the estimate was obtained (method and source readings). Required when <see cref="IsEstimate"/>.</summary>
    public string? EstimateNote { get; set; }
}
