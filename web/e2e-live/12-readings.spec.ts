import type { APIRequestContext, Page } from '@playwright/test';
import { api, expect, go, loginAs, test, users } from './live';
import { TODAY, download, isCzechCsv, isXlsx, json, lastClosedDay, meters, roles } from './data';
import type { Meter, Reading } from './data';

/**
 * T04 + import odečtů: ruční zadání, import ze schránky (náhled → potvrzení, duplicitní měsíc, nižší hodnota),
 * oprava v seznamu (český formát čísla), export XLSX/CSV, člen vidí jen svůj a hlavní vodoměr.
 * Každý běh si najde měsíc, ve kterém vybrané vodoměry ještě odečet nemají (odečty se nedají smazat).
 */
const ym = (iso: string) => iso.slice(0, 7);
const czShort = (iso: string) => { const [y, m, d] = iso.split('-').map(Number); return `${d}.${m}.${y}`; };

async function freeMonth(ctx: APIRequestContext, meterIds: string[], skip: string[] = []): Promise<string> {
  const readings = await json<Reading[]>(ctx, 'readings/all');
  const closed = await lastClosedDay(ctx);
  const taken = new Set(readings.filter((r) => meterIds.includes(r.meterId)).map((r) => ym(r.readingDate)));
  for (let y = 2024; y <= Number(TODAY.slice(0, 4)); y++) for (let m = 1; m <= 12; m++) {
    const month = `${y}-${String(m).padStart(2, '0')}`;
    const day = `${month}-15`;
    if (month >= TODAY.slice(0, 7) || taken.has(month) || skip.includes(month) || (closed && day <= closed)) continue;
    return day;
  }
  throw new Error('no free month left for the readings test');
}

const estimate = async (ctx: APIRequestContext, meterId: string, date: string) =>
  (await json<{ value: number }>(ctx, `readings/estimate?meterId=${meterId}&date=${date}`)).value;

async function meterFor(ctx: APIRequestContext, houseId: string): Promise<Meter> {
  return (await meters(ctx)).find((m) => m.houseId === houseId)!;
}

async function openImport(page: Page, tab: 'Ruční zadání' | 'Odečítačka (.txt / schránka)') {
  await go(page, '/readings/import');
  await expect(page.getByRole('heading', { name: 'Import odečtů' })).toBeVisible();
  await page.getByRole('button', { name: tab }).click();
}

test.describe.serial('odečty (T04, import)', () => {
  test('ruční zadání odečtu jednoho vodoměru; druhý odečet ve stejném měsíci je odmítnut', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const { others } = await roles(admin, users.member.houseId);
    const meter = await meterFor(admin, others[3].id);
    const day = await freeMonth(admin, [meter.id]);
    const value = await estimate(admin, meter.id, day);

    await loginAs(page, users.admin);
    await openImport(page, 'Ruční zadání');
    await page.locator('input[type=date]').fill(day);
    const row = page.getByRole('row').filter({ hasText: meter.meterNumber });
    await row.getByRole('textbox').fill(String(value).replace('.', ','));
    await page.getByRole('button', { name: 'Uložit odečty' }).click();
    await expect(page.getByText(/Úspěšně uloženo 1 odečtů k datu/)).toBeVisible();
    const saved = (await json<Reading[]>(admin, 'readings/all')).find((r) => r.meterId === meter.id && r.readingDate.startsWith(day));
    expect(saved?.value).toBeCloseTo(value, 3);

    // same meter, same month, another day → refused
    await page.locator('input[type=date]').fill(`${ym(day)}-20`);
    await row.getByRole('textbox').fill(String(value + 1).replace('.', ','));
    await page.getByRole('button', { name: 'Uložit odečty' }).click();
    await expect(page.getByText(/Uloženo 0 odečtů\. Chyby:/)).toBeVisible();
  });

  test('ruční zadání nižší hodnoty než předchozí odečet musí být odmítnuto (jako u importu)', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const { others } = await roles(admin, users.member.houseId);
    const meter = await meterFor(admin, others[3].id);
    const day = await freeMonth(admin, [meter.id]);
    const previous = (await json<Reading[]>(admin, 'readings/all'))
      .filter((r) => r.meterId === meter.id && r.readingDate.slice(0, 10) < day).sort((a, b) => a.readingDate.localeCompare(b.readingDate)).at(-1);
    test.skip(!previous || previous.value < 1, 'vodoměr nemá předchozí odečet');

    await loginAs(page, users.admin);
    await openImport(page, 'Ruční zadání');
    await page.locator('input[type=date]').fill(day);
    await page.getByRole('row').filter({ hasText: meter.meterNumber }).getByRole('textbox').fill('0,5');
    await page.getByRole('button', { name: 'Uložit odečty' }).click();
    const result = page.getByText(/Úspěšně uloženo|Uloženo \d+ odečtů\. Chyby/);
    await expect(result).toBeVisible();
    const text = await result.innerText();
    const saved = (await json<Reading[]>(admin, 'readings/all')).find((r) => r.meterId === meter.id && r.readingDate.startsWith(day));
    if (saved) {
      // repair: move the bad value to the interpolated one so later calculations are not broken
      await admin.put(`readings/${meter.id}/${day}`, { data: { value: await estimate(admin, meter.id, day) || previous!.value } });
    }
    test.info().annotations.push({ type: 'evidence', description: text });
    // BUG: POST /readings přijme stav nižší než předchozí odečet (záporná spotřeba); import ze schránky ho odmítá.
    test.fail(!!saved, 'ruční odečet nižší než předchozí byl uložen');
    expect(saved, 'nižší stav nesmí projít').toBeUndefined();
  });

  test('import ze schránky: náhled → potvrzení; opakování = chyba duplicitního měsíce; nižší hodnota = chyba', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const { others } = await roles(admin, users.member.houseId);
    const a = await meterFor(admin, others[4].id);
    const b = await meterFor(admin, others[5 % others.length].id);
    const day = await freeMonth(admin, [a.id, b.id]);
    const va = await estimate(admin, a.id, day);
    const vb = await estimate(admin, b.id, day);
    const text = `Reception time\tAddress\tValue 1\n${day} 10:00\t${a.meterNumber}\t${String(va).replace('.', ',')}\n${day} 10:05\t${b.meterNumber}\t${String(vb).replace('.', ',')}\n`;

    await loginAs(page, users.admin);
    await openImport(page, 'Odečítačka (.txt / schránka)');
    await page.locator('input[type=date]').fill(day);
    await page.locator('textarea').fill(text);
    await page.getByRole('button', { name: 'Načíst náhled' }).click();
    await expect(page.getByRole('heading', { name: 'Náhled importu' })).toBeVisible();
    // nothing saved yet (two-step import)
    expect((await json<Reading[]>(admin, 'readings/all')).some((r) => r.meterId === a.id && r.readingDate.startsWith(day))).toBe(false);
    await expect(page.getByText(/^Chyby \(/)).toHaveCount(0);
    await page.getByRole('button', { name: 'Potvrdit import' }).click();
    await expect(page.getByText('Import byl úspěšný. Importováno 2 odečtů.')).toBeVisible();
    const after = await json<Reading[]>(admin, 'readings/all');
    expect(after.find((r) => r.meterId === a.id && r.readingDate.startsWith(day))?.value).toBeCloseTo(va, 3);
    expect(after.find((r) => r.meterId === b.id && r.readingDate.startsWith(day))?.value).toBeCloseTo(vb, 3);

    // the same data again → duplicate month error, confirm disabled
    await page.getByRole('button', { name: 'Importovat další' }).click();
    await page.locator('input[type=date]').fill(`${ym(day)}-28`);
    await page.locator('textarea').fill(text);
    await page.getByRole('button', { name: 'Načíst náhled' }).click();
    await expect(page.getByText(/^Chyby \(/)).toBeVisible();
    await expect(page.getByText(/měsíc|existuje/i).first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'Potvrdit import' })).toBeDisabled();
    await page.getByRole('button', { name: 'Zrušit' }).click();

    // a lower value than the previous reading → error
    const next = await freeMonth(admin, [a.id], [ym(day)]);
    await page.locator('input[type=date]').fill(next);
    await page.locator('textarea').fill(`Address\tValue 1\n${a.meterNumber}\t0,1\n`);
    await page.getByRole('button', { name: 'Načíst náhled' }).click();
    await expect(page.getByText(/^Chyby \(/)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Potvrdit import' })).toBeDisabled();

    // an unknown meter → error
    await page.getByRole('button', { name: 'Zrušit' }).click();
    await page.locator('textarea').fill('Address\tValue 1\nNEEXISTUJE-999\t10\n');
    await page.getByRole('button', { name: 'Načíst náhled' }).click();
    await expect(page.getByText(/^Chyby \(/)).toBeVisible();
  });

  test('seznam odečtů: oprava hodnoty kliknutím, český formát „1 234,567“', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const { others } = await roles(admin, users.member.houseId);
    const meter = await meterFor(admin, others[3].id);
    const readings = (await json<Reading[]>(admin, 'readings/all')).filter((r) => r.meterId === meter.id).sort((x, y) => x.readingDate.localeCompare(y.readingDate));
    const closed = await lastClosedDay(admin);
    const target = readings.filter((r) => !closed || r.readingDate.slice(0, 10) > closed).at(-1)!;
    const day = target.readingDate.slice(0, 10);
    const newValue = Math.round(target.value * 1000) / 1000;
    const typed = newValue.toLocaleString('cs-CZ', { minimumFractionDigits: 3, maximumFractionDigits: 3 });

    await loginAs(page, users.admin);
    await go(page, '/readings/list');
    await expect(page.getByRole('heading', { name: 'Seznam odečtů' })).toBeVisible();
    await expect(page.locator('.animate-spin')).toHaveCount(0, { timeout: 30_000 });
    const headers = await page.getByRole('row').first().getByRole('columnheader').allInnerTexts();
    const col = headers.findIndex((h) => h.trim() === czShort(day));
    expect(col, `sloupec ${day}`).toBeGreaterThan(0);
    const row = page.getByRole('row').filter({ hasText: meter.meterNumber });
    await row.getByRole('cell').nth(col).click();
    const input = row.locator('input[inputmode=decimal]');
    await input.fill(typed);
    await row.getByRole('button', { name: 'OK' }).click();
    await expect(page.getByText(/^Uloženo:/)).toBeVisible();
    const saved = (await json<Reading[]>(admin, 'readings/all')).find((r) => r.meterId === meter.id && r.readingDate.startsWith(day));
    expect(saved?.value).toBeCloseTo(newValue, 3);
  });

  test('export odečtů XLSX a CSV (soubor se stáhne s příponou a správným obsahem)', async ({ page }) => {
    await loginAs(page, users.admin);
    await go(page, '/readings/list');
    const section = page.getByRole('region', { name: 'Export odečtů' });
    await section.getByLabel('Od').fill('2024-01-01');
    await section.getByLabel('Do').fill(TODAY);
    const xlsx = await download(page, () => section.getByRole('button', { name: 'Export odečtů (XLSX)' }).click(), /\.xlsx$/);
    expect(isXlsx(xlsx.body)).toBe(true);
    const csv = await download(page, () => section.getByRole('button', { name: 'CSV' }).click(), /\.csv$/);
    expect(isCzechCsv(csv.body)).toBe(true);
    expect(csv.body.toString('utf-8')).toContain('Odhad');
    test.info().annotations.push({ type: 'evidence', description: `názvy souborů: ${xlsx.name}, ${csv.name}` });
    expect(xlsx.name, 'název souboru XLSX').toMatch(/\.xlsx$/);
    expect(csv.name, 'název souboru CSV').toMatch(/\.csv$/);
  });

  test('člen vidí na stránce Odečty jen svůj a hlavní vodoměr', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const all = await meters(admin);
    const allowed = all.filter((m) => m.type === 'Main' || m.houseId === users.member.houseId);
    await loginAs(page, users.member);
    const response = page.waitForResponse((r) => r.url().includes('/readings/all') && r.request().method() === 'GET');
    await go(page, '/readings');
    const body: Reading[] = await (await response).json();
    expect(new Set(body.map((r) => r.meterId))).toEqual(new Set(allowed.map((m) => m.id)));
    await page.getByRole('button', { name: 'Od začátku' }).click();
    await expect(page.getByText(new RegExp(`^${allowed.length} vodoměrů ×`))).toBeVisible();
    for (const m of all.filter((x) => !allowed.includes(x))) await expect(page.getByText(m.meterNumber, { exact: false })).toHaveCount(0);
  });

  test('odhad k datu (API): přesný odečet, interpolace, bez extrapolace za poslední odečet', async ({ playwright }) => {
    const admin = await api(playwright, users.admin);
    const main = (await meters(admin)).find((m) => m.type === 'Main')!;
    const rs = (await json<Reading[]>(admin, 'readings/all')).filter((r) => r.meterId === main.id).sort((a, b) => a.readingDate.localeCompare(b.readingDate));
    const exact = await json<{ value: number; isEstimate: boolean }>(admin, `readings/estimate?meterId=${main.id}&date=${rs[1].readingDate.slice(0, 10)}`);
    expect(exact).toMatchObject({ value: rs[1].value, isEstimate: false });
    const future = await json<{ value: number; isEstimate: boolean; note: string }>(admin, `readings/estimate?meterId=${main.id}&date=2030-01-01`);
    expect(future.value).toBe(rs.at(-1)!.value);
    expect(future.isEstimate).toBe(true);
    const member = await api(playwright, users.member);
    expect((await member.get(`readings/estimate?meterId=${main.id}&date=2025-01-01`)).status()).toBe(403);
    expect((await admin.get(`readings/estimate?meterId=${main.id}&date=nesmysl`)).status()).toBe(400);
  });
});
