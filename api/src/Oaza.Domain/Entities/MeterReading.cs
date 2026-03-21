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
}
