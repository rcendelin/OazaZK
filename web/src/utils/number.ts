/**
 * Parses a number typed in Czech locale format (cs-CZ): comma as decimal
 * separator, space (incl. NBSP/narrow NBSP) as thousands separator.
 * Strips all whitespace before parsing so "2 500" and "2 500,50" parse
 * correctly instead of truncating at the separator.
 */
export function parseCzechNumber(input: string): number {
  const v = parseFloat(input.replace(/\s/g, '').replace(',', '.'));
  return Number.isNaN(v) ? 0 : v;
}
