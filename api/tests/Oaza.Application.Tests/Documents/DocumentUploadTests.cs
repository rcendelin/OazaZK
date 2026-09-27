using FluentAssertions;
using Oaza.Application.Documents;
using Oaza.Application.Tests.TestSupport;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.Documents;

public class DocumentUploadTests
{
    private const long MB = 1024 * 1024;

    [Theory]
    [InlineData("faktury", "application/pdf", 10 * MB, null)]
    [InlineData("faktury", "image/jpeg", 1_000, null)]
    [InlineData("faktury", "image/png", 20 * MB, null)]
    [InlineData("faktury", "application/pdf", 20 * MB + 1, "Soubor je větší než 20 MB.")]
    [InlineData("faktury", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 1_000, "Faktury a vyúčtování nahrávejte jako PDF nebo obrázek (JPG, PNG).")]
    [InlineData("smlouvy", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1_000, null)]
    [InlineData("smlouvy", "application/zip", 1_000, "Typ souboru 'application/zip' není povolen.")]
    [InlineData("faktury", "application/pdf", 0, "Soubor je prázdný.")]
    [InlineData("tajne", "application/pdf", 1_000, "Neplatná kategorie.")]
    public void UploadRules_AtLeast10MbPerFile_InvoicesPdfOrImagesOnly(string category, string contentType, long size, string? expected)
    {
        DocumentUploadRules.Check(category, contentType, size).Should().Be(expected);
    }

    [Fact]
    public void ExtensionsFollowTheContentType()
    {
        DocumentUploadRules.Extension("application/pdf").Should().Be(".pdf");
        DocumentUploadRules.Extension("image/png").Should().Be(".png");
        DocumentUploadRules.Extension("x/unknown").Should().Be(".bin");
    }

    private sealed class MemoryDocuments() : MemoryRepo<Document>(d => d.Category, d => d.Id), IDocumentRepository
    {
        public Task<IReadOnlyList<Document>> GetByCategoryAsync(string category) => GetByPartitionKeyAsync(category);
    }

    [Fact]
    public async Task UnaccountedDocumentsAreInvoicesNoCostEntryRefersTo()
    {
        var documents = new MemoryDocuments();
        var components = new MemoryComponents();
        var entries = new MemoryCostEntries();
        await components.UpsertAsync(new CostComponent { Id = "vodarna", Name = "Elektřina – vodárna" });
        await documents.UpsertAsync(new Document { Id = "d1", Category = "faktury", Name = "PRE 10/2026", ComponentId = "vodarna", UploadedAt = new DateTime(2026, 10, 5) });
        await documents.UpsertAsync(new Document { Id = "d2", Category = "faktury", Name = "PRE 11/2026", UploadedAt = new DateTime(2026, 11, 5) });
        await documents.UpsertAsync(new Document { Id = "d3", Category = "smlouvy", Name = "Smlouva PRE" });
        await entries.UpsertAsync(new CostEntry { Id = "e", ComponentId = "vodarna", DocumentId = "d2" });

        var result = await new UnaccountedDocumentsUseCase(documents, components, entries).ListAsync();

        result.Should().ContainSingle();
        result[0].Document.Id.Should().Be("d1");
        result[0].Document.ComponentId.Should().Be("vodarna");
        result[0].ComponentName.Should().Be("Elektřina – vodárna");
    }
}
