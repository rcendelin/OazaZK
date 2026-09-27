import type { APIRequestContext } from '@playwright/test';
import { RUN, api, expect, go, loginAs, test, users } from './live';
import { activeHouses, json, lastClosedDay, round2 } from './data';

/**
 * T05 Voda a ztráty, platby (záloha / doplatek / výplata, mazání, člen jen čte) a doporučené zálohy s ručním přepisem.
 */
interface WaterHouse { houseId: string; houseName: string; consumptionM3: number; waterCost: number; lossCost: number; isEstimate: boolean }
interface WaterInterval { from: string; to: string; days: number; mainConsumptionM3: number; housesConsumptionM3: number; lossM3: number; pricePerM3: number | null; waterCost: number; lossCost: number; allocated: boolean; lossSegments: { method: string }[]; warnings: string[]; houses: WaterHouse[] }
interface Water { intervals: WaterInterval[]; totals: WaterHouse[] }
interface Payment { houseId: string; rowKey: string; type: string; year: number; month: number; amount: number; waterAmount: number; electricityAmount: number; commonAmount: number; paymentDate: string; note: string | null }
interface Calc { from: string; to: string; months: number; houses: { houseId: string; houseName: string; costsInPeriod: Record<string, number>; recommended: Record<string, number>; actual: Record<string, number>; hasOverride: boolean }[] }

const TAG = `${RUN}-${Date.now().toString(36).slice(-4)}`;
const payments = (ctx: APIRequestContext, houseId?: string) => json<Payment[]>(ctx, houseId ? `advances?houseId=${houseId}` : 'advances');

test.describe.serial('voda a ztráty (T05)', () => {
  test('úseky mezi odečty hlavního vodoměru: ztráta = hlavní − domy, cena z faktur, náklady domů sedí na součty', async ({ playwright }) => {
    const admin = await api(playwright, users.admin);
    const w = await json<Water>(admin, 'water-settlement?from=2023-11-01&to=2026-09-27');
    expect(w.intervals.length).toBeGreaterThan(1);
    const problems: string[] = [];
    for (const i of w.intervals) {
      if (Math.abs(i.mainConsumptionM3 - i.housesConsumptionM3 - i.lossM3) > 0.001) problems.push(`${i.from}: ztráta ${i.lossM3} ≠ ${i.mainConsumptionM3} − ${i.housesConsumptionM3}`);
      if (Math.abs(i.houses.reduce((s, h) => s + h.consumptionM3, 0) - i.housesConsumptionM3) > 0.001) problems.push(`${i.from}: Σ spotřeb domů ≠ domy celkem`);
      if (i.allocated) {
        const water = round2(i.houses.reduce((s, h) => s + h.waterCost, 0));
        const loss = round2(i.houses.reduce((s, h) => s + h.lossCost, 0));
        if (Math.abs(water - i.waterCost) > 0.011) problems.push(`${i.from}: Σ voda domů ${water} ≠ ${i.waterCost}`);
        if (i.lossM3 > 0 && Math.abs(loss - i.lossCost) > 0.011) problems.push(`${i.from}: Σ ztráty domů ${loss} ≠ ${i.lossCost}`);
        if (i.pricePerM3 !== null && Math.abs(i.waterCost - round2(i.housesConsumptionM3 * i.pricePerM3)) > 0.05)
          problems.push(`${i.from}: voda ${i.waterCost} ≠ ${i.housesConsumptionM3} m³ × ${i.pricePerM3}`);
      }
      if (i.lossM3 < 0 && i.warnings.length === 0) problems.push(`${i.from}: záporná ztráta bez varování`);
      if (i.from >= '2026-01-01' && i.lossM3 > 0 && i.lossSegments.length > 0 && !i.lossSegments.every((s) => s.method === 'Ratio')) problems.push(`${i.from}: ztráty 2026 nejsou poměrem`);
    }
    const priced = w.intervals.filter((i) => i.pricePerM3 !== null);
    expect(priced.length, 'úseky s cenou z faktur PVK').toBeGreaterThan(0);
    expect(problems, problems.join('\n')).toEqual([]);
  });

  test('stránka Voda a ztráty: filtr období („Zobrazit“) načte jiné úseky, karty a součty jsou vidět', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const long = await json<Water>(admin, 'water-settlement?from=2023-11-01&to=2026-09-27');
    await loginAs(page, users.admin);
    await go(page, '/voda');
    await expect(page.getByRole('heading', { name: 'Voda a ztráty' })).toBeVisible();
    await expect(page.locator('.animate-spin')).toHaveCount(0, { timeout: 30_000 });
    await page.getByLabel('Od', { exact: true }).fill('2023-11-01');
    await page.getByLabel('Do', { exact: true }).fill('2026-09-27');
    const reload = page.waitForResponse((r) => r.url().includes('/water-settlement?from=2023-11-01'));
    await page.getByRole('button', { name: 'Zobrazit' }).click();
    await reload;
    const cards = page.getByRole('region', { name: /^Úsek / });
    await expect(cards).toHaveCount(long.intervals.length);
    const firstPriced = long.intervals.find((i) => i.pricePerM3 !== null)!;
    const card = cards.nth(long.intervals.indexOf(firstPriced));
    await expect(card).toContainText(firstPriced.pricePerM3!.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).replace(/\s/g, ' '));
    await expect(page.getByRole('region', { name: 'Součty za období' })).toBeVisible();
    if (long.intervals.some((i) => i.lossM3 < 0)) await expect(page.getByText('nerozpočteno').first()).toBeVisible();
  });
});

test.describe.serial('platby a zálohy', () => {
  let houseName = '';
  test('záloha za měsíc (předvyplnění předepsané zálohy), duplicita odmítnuta, doplatek, výplata a smazání výplaty', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const house = (await activeHouses(admin)).find((h) => h.id === users.member.houseId)!;
    houseName = house.name;
    const closed = await lastClosedDay(admin);
    const existing = await payments(admin, house.id);
    const month = [9, 8, 7, 6, 5, 4, 3, 2, 1].map((m) => ({ y: 2026, m })).concat([12, 11, 10].map((m) => ({ y: 2025, m })))
      .find(({ y, m }) => !existing.some((p) => p.type === 'Advance' && p.year === y && p.month === m) && (!closed || `${y}-${String(m).padStart(2, '0')}-01` > closed))!;

    await loginAs(page, users.admin);
    await go(page, '/saldo');
    await expect(page.getByRole('heading', { name: 'Platby', exact: true })).toBeVisible();
    await expect(page.locator('.animate-spin')).toHaveCount(0, { timeout: 30_000 });
    const box = page.locator('div').filter({ has: page.getByRole('heading', { name: 'Zaznamenat platbu' }) }).last();
    await box.getByRole('button', { name: 'Měsíční záloha' }).click();
    await box.getByRole('combobox').selectOption({ label: house.name });
    await box.getByRole('spinbutton').nth(0).fill(String(month.y));
    await box.getByRole('spinbutton').nth(1).fill(String(month.m));
    await box.locator('input[type=date]').fill(`${month.y}-${String(month.m).padStart(2, '0')}-10`);
    await box.getByRole('button', { name: 'Předvyplnit předepsanou zálohu' }).click();
    const calc = await json<Calc>(admin, 'advance-settings/calculate');
    const plan = calc.houses.find((h) => h.houseId === house.id)!;
    await expect(box.locator('input[inputmode=decimal]').nth(0)).toHaveValue(String(plan.actual.water));
    // own amounts in Czech format
    await box.locator('input[inputmode=decimal]').nth(0).fill('1 000');
    await box.locator('input[inputmode=decimal]').nth(1).fill('250,5');
    await box.locator('input[inputmode=decimal]').nth(2).fill('0');
    await expect(box.getByRole('button', { name: /^Uložit \(1\s?251 Kč\)$/ })).toBeVisible();
    await box.getByRole('button', { name: /^Uložit/ }).click();
    const mm = String(month.m).padStart(2, '0');
    await expect(page.getByText(`Záloha za ${month.y}-${mm} uložena.`)).toBeVisible();
    const saved = (await payments(admin, house.id)).find((p) => p.type === 'Advance' && p.year === month.y && p.month === month.m)!;
    expect(saved).toMatchObject({ waterAmount: 1000, electricityAmount: 250.5, commonAmount: 0, amount: 1250.5 });

    // UX: after saving, the whole form re-mounts (page shows a spinner while payments reload) → house/month are lost
    const houseKept = await box.getByRole('combobox').inputValue();
    test.info().annotations.push({ type: 'evidence', description: `po uložení zůstal vybraný dům: ${houseKept ? 'ano' : 'ne'}` });

    // the same month again → 409 message
    await box.getByRole('combobox').selectOption({ label: house.name });
    await box.getByRole('spinbutton').nth(0).fill(String(month.y));
    await box.getByRole('spinbutton').nth(1).fill(String(month.m));
    await box.locator('input[inputmode=decimal]').nth(0).fill('10');
    await box.getByRole('button', { name: /^Uložit/ }).click();
    await expect(page.locator('.bg-danger-light').filter({ hasText: /existuje|už|duplic|zaznamenan/i })).toBeVisible();

    // doplatek with a note — double click must create one record only
    await box.getByRole('button', { name: 'Doplatek' }).click();
    await box.getByRole('combobox').selectOption({ label: house.name });
    await box.locator('input[inputmode=decimal]').nth(0).fill('500');
    await box.locator('input[inputmode=decimal]').nth(1).fill('');
    await box.locator('input[inputmode=decimal]').nth(2).fill('');
    await box.locator('input[maxlength="500"]').fill(`${TAG} doplatek`);
    await box.getByRole('button', { name: /^Uložit/ }).dblclick();
    await expect(page.getByText('Doplatek uložen.')).toBeVisible();
    await expect.poll(async () => (await payments(admin, house.id)).filter((p) => p.note === `${TAG} doplatek`).length).toBe(1);

    // payout, then delete it via the confirmation dialog
    await box.getByRole('button', { name: 'Výplata přeplatku' }).click();
    await box.getByRole('combobox').selectOption({ label: house.name });
    await box.locator('input[inputmode=decimal]').nth(0).fill('300');
    await box.locator('input[maxlength="500"]').fill(`${TAG} výplata`);
    await box.getByRole('button', { name: 'Uložit' }).click();
    await expect(page.getByText('Výplata přeplatku uložena.')).toBeVisible();
    const row = page.getByRole('row').filter({ hasText: `${TAG} výplata` });
    await expect(row).toContainText('Výplata');
    await row.getByRole('button', { name: 'Smazat' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toContainText('Opravdu smazat Výplata');
    await dialog.getByRole('button', { name: 'Smazat' }).click();
    await expect(page.getByText('Záznam smazán.')).toBeVisible();
    await expect(row).toHaveCount(0);
    expect((await payments(admin, house.id)).some((p) => p.note === `${TAG} výplata`)).toBe(false);

    // validation on the client: no house / zero amount
    await box.getByRole('combobox').selectOption('');
    await box.getByRole('button', { name: 'Uložit' }).click();
    await expect(page.getByText('Vyberte domácnost.')).toBeVisible();
  });

  test('člen vidí jen platby svého domu, bez formuláře a mazání', async ({ page, playwright }) => {
    const member = await api(playwright, users.member);
    const own = await payments(member);
    expect(own.length).toBeGreaterThan(0);
    expect(own.every((p) => p.houseId === users.member.houseId)).toBe(true);
    await loginAs(page, users.member);
    await go(page, '/saldo');
    await expect(page.getByRole('heading', { name: 'Zaznamenané platby' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Zaznamenat platbu' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Smazat' })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Import z banky' })).toHaveCount(0);
    const rows = page.getByRole('table').getByRole('row');
    await expect(rows).toHaveCount(own.length + 1);
    for (const text of await rows.allInnerTexts()) if (!/Datum/i.test(text)) expect(text).toContain(houseName);
  });

  test('doporučené zálohy: 12 měsíců ÷ 12 po složkách, ruční přepis a jeho zrušení; člen vidí jen svůj dům', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    houseName = (await activeHouses(admin)).find((h) => h.id === users.member.houseId)!.name;
    const calc = await json<Calc>(admin, 'advance-settings/calculate');
    expect(calc.months).toBe(12);
    const wrong: string[] = [];
    for (const h of calc.houses) {
      for (const k of ['water', 'electricity', 'common'] as const) {
        const expected = Math.max(0, Math.round(h.costsInPeriod[k] / 12));
        if (Math.abs(h.recommended[k] - expected) > 1) wrong.push(`${h.houseId} ${k}: ${h.recommended[k]} ≠ ${h.costsInPeriod[k]}/12`);
      }
      if (!h.hasOverride && h.actual.total !== h.recommended.total) wrong.push(`${h.houseId}: skutečná ≠ doporučená bez přepisu`);
    }
    expect(wrong, wrong.join('\n')).toEqual([]);
    // clean state for the member's house
    const settings = await json<{ houseOverrides: Record<string, unknown> }>(admin, 'advance-settings');
    if (settings.houseOverrides[users.member.houseId!]) {
      const next = { ...settings.houseOverrides };
      delete next[users.member.houseId!];
      await admin.put('advance-settings', { data: { houseOverrides: next } });
    }

    await loginAs(page, users.admin);
    await go(page, '/advances');
    const table = page.getByRole('table', { name: 'Zálohy domů' });
    await expect(table).toBeVisible();
    const row = table.getByRole('row').filter({ hasText: houseName });
    await row.getByRole('button', { name: 'Upravit' }).click();
    await row.getByLabel(`Voda ${houseName}`).fill('700');
    await row.getByLabel(`Elektřina ${houseName}`).fill('150');
    await row.getByLabel(`Společné ${houseName}`).fill('50,5');
    await row.getByRole('button', { name: 'Uložit' }).click();
    await expect(page.getByText('Záloha domu uložena.')).toBeVisible();
    await expect(row).toContainText('upraveno');
    const after = (await json<Calc>(admin, 'advance-settings/calculate')).houses.find((h) => h.houseId === users.member.houseId)!;
    expect(after).toMatchObject({ hasOverride: true });
    expect(after.actual.water).toBe(700);
    test.info().annotations.push({ type: 'evidence', description: `přepis 50,5 → uloženo ${after.actual.common}` });

    // negative value is refused on the client
    await row.getByRole('button', { name: 'Upravit' }).click();
    await row.getByLabel(`Voda ${houseName}`).fill('-1');
    await row.getByRole('button', { name: 'Uložit' }).click();
    await expect(page.getByText('Záloha nesmí být záporná.')).toBeVisible();
    await row.getByRole('button', { name: 'Zrušit', exact: true }).click();

    // member sees only their own row while the override is active
    const memberPage = await page.context().browser()!.newPage();
    await loginAs(memberPage, users.member);
    await go(memberPage, '/advances');
    const mt = memberPage.getByRole('table', { name: 'Zálohy domů' });
    await expect(mt.getByRole('row')).toHaveCount(3); // 2 header rows + own house
    await expect(mt).toContainText(houseName);
    await expect(mt.getByRole('button', { name: 'Upravit' })).toHaveCount(0);
    await memberPage.close();

    await row.getByRole('button', { name: 'Zrušit úpravu' }).click();
    await expect(page.getByText('Záloha domu vrácena na doporučenou.')).toBeVisible();
    await expect(row).not.toContainText('upraveno');
  });
});
