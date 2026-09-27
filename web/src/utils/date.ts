/**
 * Calendar days in the association's time zone (X5). Accounting dates are days in
 * Europe/Prague for every user, whatever the browser's zone — never derive "today"
 * from `toISOString()`, which is the UTC day (yesterday in Prague between midnight
 * and 1:00, or 2:00 in summer).
 */
const PRAGUE_DAY = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Europe/Prague',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

/** The calendar day of an instant in Europe/Prague as `yyyy-MM-dd`. */
export function pragueIsoDate(instant: Date): string {
  return PRAGUE_DAY.format(instant);
}

/** Today in Europe/Prague as `yyyy-MM-dd`. */
export function todayIso(): string {
  return pragueIsoDate(new Date());
}

/** Shifts a `yyyy-MM-dd` day by whole days or months, staying on calendar days (no time zone involved). */
export function shiftIsoDate(iso: string, { days = 0, months = 0 }: { days?: number; months?: number }): string {
  const [y, m, d] = iso.split('-').map(Number);
  const shifted = new Date(Date.UTC(y, m - 1 + months, d + days));
  return shifted.toISOString().slice(0, 10);
}
