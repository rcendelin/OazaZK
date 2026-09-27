using Oaza.Application.DTOs;
using Oaza.Application.Mapping;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Documents;

/// <summary>An uploaded invoice with the component it was assigned to.</summary>
public record UnaccountedDocumentResponse(DocumentResponse Document, string? ComponentName);

/// <summary>
/// „Dokumenty bez zaúčtování“ (T11): invoices and settlements uploaded to Documents (category <c>faktury</c>) that no
/// cost entry refers to yet — what the administrator still has to book.
/// </summary>
public class UnaccountedDocumentsUseCase
{
    private readonly IDocumentRepository _documents;
    private readonly ICostComponentRepository _components;
    private readonly ICostEntryRepository _entries;

    public UnaccountedDocumentsUseCase(IDocumentRepository documents, ICostComponentRepository components, ICostEntryRepository entries)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
    }

    public async Task<IReadOnlyList<UnaccountedDocumentResponse>> ListAsync()
    {
        var components = await _components.GetAllComponentsAsync();
        var booked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in components)
        {
            foreach (var entry in await _entries.GetByComponentAsync(component.Id))
            {
                if (entry.DocumentId is not null)
                    booked.Add(entry.DocumentId);
            }
        }

        var names = components.ToDictionary(c => c.Id, c => c.Name);
        return (await _documents.GetByPartitionKeyAsync(DocumentUploadRules.InvoicesCategory))
            .Where(d => !booked.Contains(d.Id))
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new UnaccountedDocumentResponse(
                EntityMapper.ToResponse(d),
                d.ComponentId is null ? null : names.GetValueOrDefault(d.ComponentId)))
            .ToList();
    }
}
