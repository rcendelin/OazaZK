namespace Oaza.Application.Documents;

/// <summary>
/// What may be uploaded to Documents. Invoices and settlements from the supplier (category <c>faktury</c>, T11) are
/// PDFs or photos only; the other categories also accept Word and Excel. At most 20 MB per file (T11 asks ≥ 10 MB).
/// </summary>
public static class DocumentUploadRules
{
    public const string InvoicesCategory = "faktury";
    public const long MaxFileSizeBytes = 20 * 1024 * 1024;

    public static readonly string[] Categories = ["stanovy", "zapisy", "smlouvy", InvoicesCategory, "ostatni"];

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
    };

    private static readonly HashSet<string> InvoiceTypes = new(StringComparer.OrdinalIgnoreCase) { "application/pdf", "image/jpeg", "image/png" };

    /// <returns>Why the file cannot be uploaded (Czech), or null when it can.</returns>
    public static string? Check(string? category, string? contentType, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(category) || !Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
            return "Neplatná kategorie.";
        if (sizeBytes <= 0)
            return "Soubor je prázdný.";
        if (sizeBytes > MaxFileSizeBytes)
            return $"Soubor je větší než {MaxFileSizeBytes / 1024 / 1024} MB.";
        if (contentType is null || !Extensions.ContainsKey(contentType))
            return $"Typ souboru '{contentType}' není povolen.";
        if (string.Equals(category, InvoicesCategory, StringComparison.OrdinalIgnoreCase) && !InvoiceTypes.Contains(contentType))
            return "Faktury a vyúčtování nahrávejte jako PDF nebo obrázek (JPG, PNG).";
        return null;
    }

    public static string Extension(string contentType) => Extensions.GetValueOrDefault(contentType, ".bin");
}
