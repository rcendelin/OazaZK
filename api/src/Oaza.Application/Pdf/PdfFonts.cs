using System.Reflection;
using PdfSharpCore.Fonts;

namespace Oaza.Application.Pdf;

/// <summary>
/// Fonts for the PDF exports, embedded in the assembly (DejaVu Sans — free license, Czech glyphs). PdfSharpCore looks up
/// system fonts by default and the Azure Functions Flex Consumption image has none, so every PDF export failed with 500
/// there. Any requested family (e.g. „Arial“) is served by DejaVu Sans; register once before creating a document.
/// </summary>
public sealed class PdfFonts : IFontResolver
{
    public const string Family = "DejaVu Sans";
    private const string Regular = "DejaVuSans";
    private const string Bold = "DejaVuSans-Bold";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, byte[]> Cache = [];

    public string DefaultFontName => Family;

    /// <summary>Makes PdfSharpCore use the embedded fonts (idempotent, thread-safe).</summary>
    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (GlobalFontSettings.FontResolver is not PdfFonts)
                GlobalFontSettings.FontResolver = new PdfFonts();
        }
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? Bold : Regular, false, isItalic);

    public byte[] GetFont(string faceName)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(faceName, out var cached))
                return cached;
            var name = $"Oaza.Application.Pdf.Fonts.{faceName}.ttf";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Chybí vložené písmo {name}.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return Cache[faceName] = memory.ToArray();
        }
    }
}
