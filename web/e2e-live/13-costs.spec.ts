import type { APIRequestContext, Page } from '@playwright/test';
import { RUN, api, expect, go, kc, loginAs, test, users } from './live';
import { componentByCode, componentDetail, json, round2 } from './data';

/**
 * T06 Náklady: faktury PVK s m³, série záloh z kreditu (S2), vyúčtování přes změnu účasti (S3), jednorázový náklad,
 * rozpad na domy (Σ = částka), úprava (jen API — UI ji nenabízí) a smazání, validace, účetní jen čte.
 */
interface Entry { id: string; type: string; periodFrom: string; periodTo: string; amount: number; quantityM3: number | null; supplier: string | null; paidFrom: string; note: string | null; locked: boolean }
interface Allocation { houseTotals: { houseId: string; houseName: string; amount: number }[]; segments: { from: string; to: string; days: number; amount: number; shares: unknown[] }[] }

const SETUP = 'E2E-SETUP';
const pvkInvoices = [
  ['2023-11-01', '2024-04-30', 12_000, 200],
  ['2024-05-01', '2024-10-31', 13_200, 220],
  ['2024-11-01', '2025-04-30', 11_400, 190],
  ['2025-05-01', '2025-10-31', 15_000, 240],
  ['2025-11-01', '2026-04-30', 12_600, 200],
] as const;
const UI_INVOICE = ['2026-05-01', '2026-08-31', '9 750,50', '150,5'] as const;

const entries = (ctx: APIRequestContext, componentId: string, from = '2023-01-01', to = '2027-12-31') =>
  json<Entry[]>(ctx, `cost-components/${componentId}/entries?from=${from}&to=${to}`);

async function openCosts(page: Page, componentName: string) {
  await go(page, '/naklady');
  await expect(page.getByRole('heading', { name: 'Náklady', exact: true })).toBeVisible();
  await page.getByRole('tab', { name: componentName, exact: true }).click();
  await expect(page.getByRole('tab', { name: componentName, exact: true })).toHaveAttribute('aria-selected', 'true');
  return page.getByRole('region', { name: `Náklady ${componentName}` });
}

async function showRange(section: ReturnType<Page['getByRole']>, from: string, to: string) {
  await section.getByLabel('Od', { exact: true }).first().fill(from);
  await section.getByLabel('Do', { exact: true }).first().fill(to);
  await section.getByRole('button', { name: 'Zobrazit' }).click();
  await expect(section.locator('.animate-spin')).toHaveCount(0);
}

test.describe.serial('náklady (T06)', () => {
  test('faktury PVK: jedna přes formulář (český formát částky i m³), ostatní set-up přes API; průměrná cena za m³', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const pvk = (await componentByCode(admin, 'VODA_PVK'))!;
    const have = await entries(admin, pvk.id);
    for (const [from, to, amount, m3] of pvkInvoices) {
      if (have.some((e) => e.periodFrom.startsWith(from) && e.note === SETUP)) continue;
      const res = await admin.post(`cost-components/${pvk.id}/entries`, { data: { type: 'OneOff', periodFrom: from, periodTo: to, amount, quantityM3: m3, supplier: 'PVK', paidFrom: 'Bank', note: SETUP } });
      expect(res.status(), await res.text()).toBe(201);
    }
    for (const e of have.filter((x) => x.periodFrom.startsWith(UI_INVOICE[0]))) await admin.delete(`cost-components/${pvk.id}/entries/${e.id}?reason=rerun`);

    await loginAs(page, users.admin);
    const section = await openCosts(page, pvk.name);
    await expect(section).toContainText('Zde se zadávají faktury dodavatele s fakturovanými m³');
    const form = section.getByRole('form', { name: 'Přidat náklad' });
    await expect(form.getByRole('heading', { name: 'Přidat fakturu' })).toBeVisible();
    await form.getByLabel('Období od').fill(UI_INVOICE[0]);
    await form.getByLabel('do', { exact: true }).fill(UI_INVOICE[1]);
    await form.getByLabel('Částka (Kč)').fill(UI_INVOICE[2]);
    await form.getByLabel('Množství (m³)').fill(UI_INVOICE[3]);
    await form.getByLabel('Dodavatel', { exact: true }).fill('PVK');
    await form.getByLabel('Poznámka').fill(`${RUN} faktura`);
    await form.getByRole('button', { name: 'Přidat' }).click();
    await expect(form.getByRole('alert')).toHaveCount(0);
    const table = section.getByRole('table', { name: 'Nákladové záznamy' });
    const row = table.getByRole('row').filter({ hasText: '1. 5. 2026 – 31. 8. 2026' });
    await expect(row).toContainText(/9\s?750,50\s?Kč/);
    await expect(row).toContainText('150,5');
    const saved = (await entries(admin, pvk.id)).find((e) => e.periodFrom.startsWith(UI_INVOICE[0]))!;
    expect(saved).toMatchObject({ amount: 9750.5, quantityM3: 150.5, type: 'OneOff' });
    // footer = Σ Kč and average price per m³
    await showRange(section, '2023-11-01', '2026-09-27');
    const all = (await entries(admin, pvk.id)).filter((e) => e.periodFrom >= '2023-11-01');
    const total = all.reduce((s, e) => s + e.amount, 0);
    const qty = all.reduce((s, e) => s + (e.quantityM3 ?? 0), 0);
    const foot = table.locator('tfoot');
    await expect(foot).toContainText(kc(round2(total)));
    await expect(foot).toContainText('průměrná cena');
    await expect(foot).toContainText(kc(round2(total / qty)));
  });

  test('série měsíčních záloh z kreditu (S2): 12 × 500 Kč, rozpad na 4 domy po 125 Kč', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const el = (await componentByCode(admin, 'ELEKTRINA_VODARNA'))!;
    for (const e of (await entries(admin, el.id, '2023-11-01', '2024-10-31')).filter((x) => x.type === 'Advance' && x.paidFrom === 'SupplierCredit'))
      await admin.delete(`cost-components/${el.id}/entries/${e.id}?reason=rerun`);

    await loginAs(page, users.admin);
    const section = await openCosts(page, el.name);
    const form = section.getByRole('form', { name: 'Opakovaná záloha' });
    await form.getByLabel('Částka (Kč)').fill('500');
    await form.getByLabel('Perioda').selectOption('Monthly');
    await form.getByLabel('Od', { exact: true }).fill('2023-11-01');
    await form.getByLabel('Do', { exact: true }).fill('2024-10-31');
    await form.getByLabel('Dodavatel', { exact: true }).fill('Dodavatel elektřiny');
    await form.getByLabel('Úhrada').selectOption('SupplierCredit');
    await form.getByRole('button', { name: 'Vytvořit zálohy' }).click();
    await expect(form.getByRole('status')).toHaveText('Vytvořeno 12 záloh.');

    await showRange(section, '2023-11-01', '2024-10-31');
    const table = section.getByRole('table', { name: 'Nákladové záznamy' });
    await expect(table.getByRole('row').filter({ hasText: 'Z přeplatku u dodavatele' })).toHaveCount(12);
    const first = table.getByRole('row').filter({ hasText: '1. 11. 2023 – 30. 11. 2023' });
    await first.getByRole('button', { name: 'Rozpad na domy' }).click();
    const totals = section.getByRole('table', { name: 'Součty za domy' });
    await expect(totals.getByRole('row')).toHaveCount(4);
    for (const r of await totals.getByRole('row').all()) await expect(r).toContainText(/125,00\s?Kč/);
    const series = await entries(admin, el.id, '2023-11-01', '2024-10-31');
    const alloc = await json<Allocation>(admin, `cost-components/${el.id}/entries/${series[0].id}/allocation`);
    expect(round2(alloc.houseTotals.reduce((s, h) => s + h.amount, 0))).toBe(500);
  });

  test('vyúčtování osvětlení 1 840 Kč přes připojení domu (S3 analogie): úseky po dnech, Σ domů = částka', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const osv = (await componentByCode(admin, 'OSVETLENI'))!;
    for (const e of (await entries(admin, osv.id, '2025-07-01', '2025-12-31')).filter((x) => x.type === 'Settlement'))
      await admin.delete(`cost-components/${osv.id}/entries/${e.id}?reason=rerun`);
    const detail = await componentDetail(admin, osv.id);
    const late = detail.participations.find((p) => p.validFrom === '2025-10-01')!;
    const n = detail.participations.filter((p) => p.validFrom <= '2025-12-31' && (!p.validTo || p.validTo >= '2025-07-01')).length;

    await loginAs(page, users.admin);
    const section = await openCosts(page, osv.name);
    const form = section.getByRole('form', { name: 'Přidat náklad' });
    await form.getByLabel('Druh').selectOption('Settlement');
    await expect(form.getByText('Vyúčtování: doplatek zadejte kladně, přeplatek záporně.')).toBeVisible();
    await form.getByLabel('Období od').fill('2025-07-01');
    await form.getByLabel('do', { exact: true }).fill('2025-12-31');
    await form.getByLabel('Částka (Kč)').fill('1 840,00');
    await form.getByLabel('Dodavatel', { exact: true }).fill('Dodavatel elektřiny');
    await form.getByLabel('Poznámka').fill(`${RUN} S3`);
    await form.getByRole('button', { name: 'Přidat' }).click();
    await expect(form.getByRole('alert')).toHaveCount(0);

    await showRange(section, '2025-07-01', '2025-12-31');
    const row = section.getByRole('table', { name: 'Nákladové záznamy' }).getByRole('row').filter({ hasText: '1. 7. 2025 – 31. 12. 2025' });
    await row.getByRole('button', { name: 'Rozpad na domy' }).click();
    const totals = section.getByRole('table', { name: 'Součty za domy' });
    await expect(totals.getByRole('row')).toHaveCount(n);
    await expect(totals.getByRole('row').filter({ hasText: late.houseName })).toContainText(/115,00\s?Kč/); // 920 / 8
    await section.getByText('Výpočet po úsecích').click();
    // segments are cut by month: July … September among n−1 houses, October … December among n houses
    await expect(section.getByText(new RegExp(`1\\. 7\\. 2025 – 31\\. 7\\. 2025 \\(31 dní z 184\\) = 310,00\\s?Kč, rovným dílem mezi ${n - 1} domy`))).toBeVisible();
    await expect(section.getByText(new RegExp(`1\\. 10\\. 2025 – 31\\. 10\\. 2025 \\(31 dní z 184\\) = 310,00\\s?Kč, rovným dílem mezi ${n} domy`))).toBeVisible();
    // houses with identical participation should end with (almost) identical totals
    const sums = (await totals.getByRole('row').allInnerTexts()).filter((t) => !t.includes(late.houseName))
      .map((t) => Number(/([\d\s ]+,\d{2})\s?Kč/.exec(t)![1].replace(/\s/g, '').replace(',', '.')));
    test.info().annotations.push({ type: 'evidence', description: `součty domů se stejnou účastí: ${sums.join(' / ')}` });
    const e = (await entries(admin, osv.id, '2025-07-01', '2025-12-31')).find((x) => x.type === 'Settlement')!;
    const alloc = await json<Allocation>(admin, `cost-components/${osv.id}/entries/${e.id}/allocation`);
    expect(round2(alloc.houseTotals.reduce((s, h) => s + h.amount, 0))).toBe(1840);
  });

  test('jednorázový náklad: přidat, upravit v UI, smazat s potvrzením a důvodem', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const osv = (await componentByCode(admin, 'OSVETLENI'))!;
    await loginAs(page, users.admin);
    const section = await openCosts(page, osv.name);
    // leftovers of an interrupted earlier run
    for (const old of (await entries(admin, osv.id, '2026-06-15', '2026-06-15')).filter((x) => x.note?.endsWith(' jednorázový')))
      await admin.delete(`cost-components/${osv.id}/entries/${old.id}?reason=cleanup`);
    await section.getByRole('button', { name: 'Zobrazit' }).click();
    const form = section.getByRole('form', { name: 'Přidat náklad' });
    await form.getByLabel('Druh').selectOption('OneOff');
    await form.getByLabel('Období od').fill('2026-06-15');
    await form.getByLabel('Částka (Kč)').fill('999');
    await form.getByLabel('Poznámka').fill(`${RUN} jednorázový`);
    await form.getByRole('button', { name: 'Přidat' }).click();
    const table = section.getByRole('table', { name: 'Nákladové záznamy' });
    const row = table.getByRole('row').filter({ hasText: '15. 6. 2026 – 15. 6. 2026' }).filter({ hasText: /999,00/ });
    await expect(row).toHaveCount(1);
    const created = (await entries(admin, osv.id, '2026-06-15', '2026-06-15')).find((x) => x.note === `${RUN} jednorázový`)!;
    expect(created).toBeTruthy();
    await row.getByRole('button', { name: 'Upravit' }).click();
    const editForm = section.getByRole('form', { name: 'Upravit náklad' });
    await editForm.getByLabel('Částka (Kč)').fill('1 234,5');
    await editForm.getByLabel('Úhrada').selectOption('Cash');
    await editForm.getByRole('button', { name: 'Uložit změnu' }).click();
    const edited = table.getByRole('row').filter({ hasText: '15. 6. 2026 – 15. 6. 2026' }).filter({ hasText: /1\s?234,50/ });
    await expect(edited).toContainText('Hotově');
    expect((await entries(admin, osv.id, '2026-06-15', '2026-06-15')).find((x) => x.id === created.id)?.amount).toBe(1234.5);
    await edited.getByRole('button', { name: 'Smazat' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toContainText('Smazat náklad?');
    await dialog.getByLabel(/Důvod/).fill(`${RUN} test`);
    await dialog.getByRole('button', { name: 'Smazat', exact: true }).click();
    await expect(edited).toHaveCount(0);
    expect((await entries(admin, osv.id, '2026-06-15', '2026-06-15')).some((x) => x.id === created.id)).toBe(false);
  });

  test('validace nákladů: před startem složky, nulová/záporná záloha, m³ u neměřené složky, faktura PVK bez m³', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const osv = (await componentByCode(admin, 'OSVETLENI'))!;
    const pvk = (await componentByCode(admin, 'VODA_PVK'))!;
    await loginAs(page, users.admin);
    const section = await openCosts(page, osv.name);
    const form = section.getByRole('form', { name: 'Přidat náklad' });
    await form.getByLabel('Druh').selectOption('OneOff');
    await form.getByLabel('Období od').fill('2023-10-01');
    await form.getByLabel('Částka (Kč)').fill('100');
    await form.getByRole('button', { name: 'Přidat' }).click();
    await expect(form.getByRole('alert')).toBeVisible();
    await form.getByLabel('Druh').selectOption('Advance');
    await form.getByLabel('Období od').fill('2026-01-01');
    await form.getByLabel('Částka (Kč)').fill('0');
    await form.getByRole('button', { name: 'Přidat' }).click();
    await expect(form.getByRole('alert')).toBeVisible();
    await form.getByLabel('Částka (Kč)').fill('-50');
    await form.getByRole('button', { name: 'Přidat' }).click();
    await expect(form.getByRole('alert')).toBeVisible();

    const bad = [
      await admin.post(`cost-components/${osv.id}/entries`, { data: { type: 'OneOff', periodFrom: '2026-01-01', periodTo: '2026-01-31', amount: 10, quantityM3: 5, paidFrom: 'Bank' } }),
      await admin.post(`cost-components/${pvk.id}/entries`, { data: { type: 'OneOff', periodFrom: '2026-01-01', periodTo: '2026-01-31', amount: 10, paidFrom: 'Bank' } }),
      await admin.post(`cost-components/${osv.id}/entries`, { data: { type: 'OneOff', periodFrom: '2026-02-01', periodTo: '2026-01-01', amount: 10, paidFrom: 'Bank' } }),
    ];
    const statuses = bad.map((r) => r.status());
    test.info().annotations.push({ type: 'evidence', description: `m³ u neměřené: ${statuses[0]}, PVK bez m³: ${statuses[1]}, období obráceně: ${statuses[2]}` });
    expect(statuses[1]).toBe(400);
    expect(statuses[2]).toBe(400);
    // cleanup if the API accepted m³ on a non-metered component
    if (statuses[0] === 201) await admin.delete(`cost-components/${osv.id}/entries/${(await bad[0].json()).id}?reason=cleanup`);
    expect(statuses[0], 'm³ u složky z nákladových záznamů má být odmítnuto').toBe(400);
  });

  test('účetní: náklady jen ke čtení (bez formulářů a mazání), rozpad na domy funguje', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const el = (await componentByCode(admin, 'ELEKTRINA_VODARNA'))!;
    await loginAs(page, users.accountant);
    const section = await openCosts(page, el.name);
    await expect(section.getByRole('form', { name: 'Přidat náklad' })).toHaveCount(0);
    await expect(section.getByRole('form', { name: 'Opakovaná záloha' })).toHaveCount(0);
    await showRange(section, '2023-11-01', '2024-10-31');
    const table = section.getByRole('table', { name: 'Nákladové záznamy' });
    await expect(table.getByRole('button', { name: 'Smazat' })).toHaveCount(0);
    await table.getByRole('button', { name: 'Rozpad na domy' }).first().click();
    await expect(section.getByRole('table', { name: 'Součty za domy' }).getByRole('row')).toHaveCount(4);
    const acc = await api(playwright, users.accountant);
    expect((await acc.post(`cost-components/${el.id}/entries`, { data: {} })).status()).toBe(403);
  });
});
