using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Azure.Functions.Worker.Http;

namespace Oaza.Functions.Endpoints;

/// <summary>
/// Writes a file download. The bytes go through <c>WriteBytesAsync</c> — replacing <c>response.Body</c> sends an empty
/// body in the isolated worker (live E2E, finding #6). The file name is sent as an ASCII fallback plus RFC 5987
/// <c>filename*</c> (non-ASCII names like „Zápis…“ made the header throw → 500, #7), and the header is exposed to the
/// SPA on another origin so it can save the file under its real name (#13).
/// </summary>
public static class FileResponse
{
    public static async Task<HttpResponseData> WriteAsync(HttpRequestData req, byte[] content, string contentType, string fileName)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        response.Headers.Add("Content-Disposition", ContentDisposition(fileName));
        response.Headers.Add("Access-Control-Expose-Headers", "Content-Disposition");
        await response.WriteBytesAsync(content);
        return response;
    }

    /// <summary><c>attachment; filename="ASCII"; filename*=UTF-8''percent-encoded</c>.</summary>
    public static string ContentDisposition(string fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
        if (name.Length == 0)
            name = "soubor";
        return $"attachment; filename=\"{Ascii(name)}\"; filename*=UTF-8''{Uri.EscapeDataString(name)}";
    }

    private static string Ascii(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(ch is >= ' ' and <= '~' && ch is not '"' and not '\\' and not ';' ? ch : '_');
        }
        return sb.ToString();
    }
}
