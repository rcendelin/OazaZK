import type { APIRequestContext, Download, Page } from '@playwright/test';
import { expect } from '@playwright/test';

/**
 * Shared lookups for the live business-flow specs (10–19). Everything is read from the API at runtime —
 * no house or person names in the test code.
 */
export interface House { id: string; name: string; isActive: boolean; contactPerson: string; email: string; address: string }
export interface Meter { id: string; meterNumber: string; name: string; type: 'Main' | 'Individual'; houseId: string | null }
export interface Component {
  id: string; name: string; code: string; startDate: string; allocationBasis: 'Metered' | 'CostEntries';
  waterRole: 'None' | 'Consumption' | 'Losses'; active: boolean; currentMethod: string | null; currentParticipants: number;
}
export interface Participation { id: string; houseId: string; houseName: string; validFrom: string; validTo: string | null; weight: number | null }
export interface ComponentDetail { component: Component; rules: { id: string; validFrom: string; validTo: string | null; method: string; reason: string | null }[]; participations: Participation[]; lastClosedDay: string | null }
export interface Reading { meterId: string; readingDate: string; value: number; consumption: number | null; isEstimate?: boolean }
export interface Closing { id: string; date: string; scope: 'All' | 'House'; houseId: string | null; canDelete: boolean }

export const START = '2023-11-01';
/** Today as an accounting day in Europe/Prague (like the app's todayIso()). */
export const TODAY = new Date().toLocaleDateString('sv-SE', { timeZone: 'Europe/Prague' });

export async function json<T>(ctx: APIRequestContext, path: string): Promise<T> {
  const res = await ctx.get(path);
  expect(res.status(), `GET ${path}: ${(await res.text()).slice(0, 300)}`).toBe(200);
  return (await res.json()) as T;
}

export const activeHouses = async (ctx: APIRequestContext) =>
  (await json<House[]>(ctx, 'houses')).filter((h) => h.isActive).sort((a, b) => a.name.localeCompare(b.name, 'cs'));

export const meters = (ctx: APIRequestContext) => json<Meter[]>(ctx, 'meters');

export async function componentByCode(ctx: APIRequestContext, code: string): Promise<Component | undefined> {
  return (await json<Component[]>(ctx, 'cost-components')).find((c) => c.code === code);
}

export const componentDetail = (ctx: APIRequestContext, id: string) => json<ComponentDetail>(ctx, `cost-components/${id}`);

/** Last closed day (any closing) or null. */
export async function lastClosedDay(ctx: APIRequestContext): Promise<string | null> {
  const closings = await json<Closing[]>(ctx, 'interim-closings');
  return closings.map((c) => c.date.slice(0, 10)).sort().at(-1) ?? null;
}

/** ISO day shift (UTC arithmetic on calendar days). */
export function shift(iso: string, days: number): string {
  const d = new Date(`${iso}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** Houses that the component set-up spec assigns special roles; other specs avoid them where it matters. */
export async function roles(ctx: APIRequestContext, memberHouseId?: string) {
  const houses = await activeHouses(ctx);
  const others = houses.filter((h) => h.id !== memberHouseId);
  // ELEKTRINA_VODARNA: the member's house + the first three others (4 houses)
  const vodarna = [houses.find((h) => h.id === memberHouseId), ...others.slice(0, 3)].filter((h): h is House => !!h).slice(0, 4);
  // OSVETLENI: the last active house joins later
  const lateJoiner = others.at(-1)!;
  return { houses, vodarna, lateJoiner, others };
}

/** Waits for a download started by `action`, checks its file name and returns its content. */
export async function download(page: Page, action: () => Promise<unknown>, ext: RegExp): Promise<{ name: string; body: Buffer; download: Download }> {
  const [dl] = await Promise.all([page.waitForEvent('download', { timeout: 45_000 }), action()]);
  expect(dl.suggestedFilename(), 'přípona staženého souboru').toMatch(ext);
  const path = await dl.path();
  const { readFileSync } = await import('node:fs');
  const body = readFileSync(path!);
  return { name: dl.suggestedFilename(), body, download: dl };
}

/** Minimal valid one-page PDF. */
export function pdf(text: string): Buffer {
  const content = `BT /F1 12 Tf 50 750 Td (${text.replace(/[()\\]/g, '')}) Tj ET`;
  const objs = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
    `<< /Length ${content.length} >>\nstream\n${content}\nendstream`,
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
  ];
  let out = '%PDF-1.4\n';
  const offsets: number[] = [];
  objs.forEach((o, i) => { offsets.push(out.length); out += `${i + 1} 0 obj\n${o}\nendobj\n`; });
  const xref = out.length;
  out += `xref\n0 ${objs.length + 1}\n0000000000 65535 f \n${offsets.map((o) => `${String(o).padStart(10, '0')} 00000 n \n`).join('')}`;
  out += `trailer\n<< /Size ${objs.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(out, 'latin1');
}

/** XLSX is a zip: "PK\x03\x04". */
export const isXlsx = (b: Buffer) => b.subarray(0, 4).equals(Buffer.from([0x50, 0x4b, 0x03, 0x04]));
export const isPdf = (b: Buffer) => b.subarray(0, 5).toString('latin1') === '%PDF-';
/** CSV for Czech Excel: UTF-8 BOM and ';'. */
export const isCzechCsv = (b: Buffer) => b.subarray(0, 3).equals(Buffer.from([0xef, 0xbb, 0xbf])) && b.toString('utf-8').includes(';');

/** Money with 2 decimals compared tolerant to float noise. */
export const round2 = (v: number) => Math.round(v * 100) / 100;
