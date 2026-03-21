using Oaza.Application.DTOs;
using Oaza.Domain.Entities;

namespace Oaza.Application.Interfaces;

public interface IImportSessionCache
{
    void Store(string sessionId, ImportSessionData data);
    ImportSessionData? Retrieve(string sessionId);
    void Remove(string sessionId);
}

public class ImportSessionData
{
    public List<MeterReading> Readings { get; set; } = new();
    public List<ImportValidationMessage> Errors { get; set; } = new();
    public List<ImportValidationMessage> Warnings { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}
