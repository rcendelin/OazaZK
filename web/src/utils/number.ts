/** Minus-like characters people paste or type (typographic minus, dashes, full-width hyphen-minus). */
const MINUS_LIKE = /[\u2212\u2012\u2013\u2014\uFE63\uFF0D]/g;
/** Every kind of space used as a thousands separator (incl. NBSP U+00A0, narrow NBSP U+202F, thin space). */
const SPACES = /[\s\u00A0\u2007\u2009\u202F]/g;
/** Optional sign, digits with at most one decimal separator (comma or dot). */
const NUMBER = /^[+-]?(\d+([.,]\d*)?|[.,]\d+)$/;

/**
 * Parses a number typed in Czech locale format (cs-CZ): comma (or dot) as decimal separator, any space
 * (incl. NBSP / narrow NBSP) as thousands separator, and a typographic minus „−“ or dash as the sign —
 * so „−20 000“, „1 542,7“ and „2 500,50“ all parse.
 *
 * Returns `null` for an empty input or anything that is not a whole number („abc“, „12abc“, „1,2,3“) —
 * callers must show a validation message instead of saving 0.
 */
export function parseCzechNumber(input: string): number | null {
  const normalized = input.replace(SPACES, '').replace(MINUS_LIKE, '-');
  if (!NUMBER.test(normalized)) return null;
  const value = Number(normalized.replace(',', '.'));
  return Number.isFinite(value) ? value : null;
}

/** Czech validation message for a field that does not contain a number. */
export function invalidNumberMessage(field: string): string {
  return `${field}: zadejte číslo, např. 1 234,5 nebo −20 000.`;
}

