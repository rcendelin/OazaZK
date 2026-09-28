/**
 * File name from a Content-Disposition header: prefers RFC 5987 `filename*=UTF-8''…` (Czech names), falls back to
 * `filename="…"`, then to `fallback`.
 */
export function fileNameFromDisposition(disposition: string | null, fallback: string): string {
  const header = disposition ?? '';
  const extended = /filename\*=UTF-8''([^;]+)/i.exec(header)?.[1];
  if (extended) {
    try {
      return decodeURIComponent(extended.trim());
    } catch {
      // malformed encoding — use the plain name
    }
  }
  return /filename="([^"]+)"/i.exec(header)?.[1] ?? fallback;
}
