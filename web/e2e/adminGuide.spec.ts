import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test } from '@playwright/test';
import type { Locator, Page } from '@playwright/test';
import { adminProcedures } from '../src/content/adminGuide';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

/**
 * „Návod pro správce“ (T12): every procedure of `src/content/adminGuide.ts` walked through the UI step by step
 * against the mocked API, asserting what gets sent. With `GUIDE_SCREENSHOTS=1` each step that names a screenshot
 * is also saved into `public/navod/` — the images on `/navod`. Without it (CI) the spec only asserts, but it still
 * checks that the walk-through produced exactly the screenshots the guide lists, so text and test can't drift.
 * All data here is fictitious.
 */

const SAVE_SCREENSHOTS = process.env.GUIDE_SCREENSHOTS === '1';
const outDir = fileURLToPath(new URL('../public/navod/', import.meta.url));

test.use({
  viewport: { width: 1280, height: 800 },
  locale: 'cs-CZ',
  timezoneId: 'Europe/Prague',
  // Date inputs render in the browser's UI language, not the page locale.
  launchOptions: { args: ['--lang=cs-CZ'], env: { ...process.env, LANG: 'cs_CZ.UTF-8', LANGUAGE: 'cs' } },
});

const guideUser = { ...adminUser, name: 'Alena Ukázková', email: 'alena@example.cz' };
const signIn = {
  '/environment': () => ({ environment: 'prod' }),
  '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
  '/auth/me': () => guideUser,
};

const houses = ['A', 'B', 'C', 'D'].map((x) => ({
  id: x, name: `RD ${x}`, address: '', contactPerson: `Majitel ${x}`, email: '', isActive: true, dissolveOverpayment: false,
}));

/** Tracks the screenshots of one procedure; `done()` checks they match the guide exactly and in order. */
function guide(page: Page, id: string) {
  const procedure = adminProcedures.find((p) => p.id === id);
  if (!procedure) throw new Error(`Procedure ${id} is missing in adminGuide.ts`);
  const taken: string[] = [];
  return {
    /** Screenshot for step `stepNo` (1-based, as shown on /navod) — the whole viewport or one region. */
    async shot(stepNo: number, region?: Locator) {
      const file = procedure.steps[stepNo - 1]?.screenshot;
      expect(file, `${id}: step ${stepNo} should have a screenshot in adminGuide.ts`).toBeTruthy();
      taken.push(file!);
      if (!SAVE_SCREENSHOTS) return;
      mkdirSync(outDir, { recursive: true });
      await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
      const path = join(outDir, file!);
      if (region) await region.screenshot({ path, animations: 'disabled' });
      else await page.screenshot({ path, animations: 'disabled' });
    },
    done() {
      expect(taken, `${id}: screenshots taken vs listed in adminGuide.ts`).toEqual(
        procedure.steps.flatMap((s) => (s.screenshot ? [s.screenshot] : [])),
      );
    },
  };
}

const content = (page: Page) => page.locator('main > div');

test('guide 1 — monthly readings: Excel import with preview → confirm, then manual entry', async ({ page }) => {
  const g = guide(page, 'odecty');
  const meters = [
    { id: 'm0', meterNumber: 'HV-01', name: 'Hlavní vodoměr', type: 'Main', houseId: null, houseName: null, radioAddress: null, installationDate: '2023-11-01T00:00:00Z' },
    ...houses.map((h) => ({
      id: `m${h.id}`, meterNumber: `V-${h.id}`, name: `Vodoměr ${h.name}`, type: 'Individual', houseId: h.id, houseName: h.name, radioAddress: null, installationDate: '2023-11-01T00:00:00Z',
    })),
  ];
  const meterValues = { m0: 1520.4, mA: 312.5, mB: 287.1, mC: 401.9, mD: 356.2 };
  let confirmed: { readings: { meterId: string; readingDate: string; value: number }[] } | null = null;
  const manual: Record<string, unknown>[] = [];
  await mockApi(page, {
    ...signIn,
    '/meters': () => meters,
    '/readings/import': () => ({
      rows: [{ readingDate: '2026-09-01T00:00:00Z', meterValues }],
      errors: [],
      warnings: [{ type: 'Anomaly', message: 'Vodoměr V-D: spotřeba je výrazně vyšší než v minulých měsících.', row: 2, meterId: 'mD' }],
    }),
    '/readings/import/confirm': (request) => {
      confirmed = request.postDataJSON();
      return { count: 5 };
    },
    '/readings': (request) => {
      if (request.method() === 'POST') manual.push(request.postDataJSON());
      return [];
    },
  });

  await signInAsAdmin(page);
  await navigate(page, '/readings/import');
  await expect(page.getByRole('heading', { name: 'Import odečtů' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Excel (.xlsx)' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Odečítačka (.txt / schránka)' })).toBeVisible();
  await g.shot(1);

  await page.locator('input[type=file]').setInputFiles({
    name: 'odecty-zari-2026.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: Buffer.from('PK'),
  });
  await expect(page.getByText('odecty-zari-2026.xlsx')).toBeVisible();
  await g.shot(2, content(page));
  await page.getByRole('button', { name: 'Importovat', exact: true }).click();

  await expect(page.getByRole('heading', { name: 'Náhled importu' })).toBeVisible();
  await expect(page.getByText('Upozornění (1)')).toBeVisible();
  await expect(page.getByRole('columnheader', { name: 'Hlavní vodoměr' })).toBeVisible();
  expect(confirmed).toBeNull(); // nothing saved before the confirmation
  await g.shot(3, content(page));

  await page.getByRole('button', { name: 'Potvrdit import' }).click();
  await expect(page.getByText('Import byl úspěšný. Importováno 5 odečtů.')).toBeVisible();
  await g.shot(4, content(page));
  expect(confirmed!.readings).toHaveLength(5);
  expect(confirmed!.readings).toContainEqual({ meterId: 'm0', readingDate: '2026-09-01T00:00:00Z', value: 1520.4 });

  await page.getByRole('button', { name: 'Ruční zadání' }).click();
  await page.locator('input[type=date]').fill('2026-09-30');
  await page.getByRole('row', { name: /Hlavní vodoměr/ }).getByPlaceholder('0,0').fill('1 531,8');
  await page.getByRole('row', { name: /RD A/ }).getByPlaceholder('0,0').fill('314,2');
  await g.shot(5, content(page));
  await page.getByRole('button', { name: 'Uložit odečty' }).click();
  await expect(page.getByText('Úspěšně uloženo 2 odečtů k datu 30. 9. 2026.')).toBeVisible();
  expect(manual).toEqual([
    { meterId: 'm0', readingDate: '2026-09-30T00:00:00.000Z', value: 1531.8 },
    { meterId: 'mA', readingDate: '2026-09-30T00:00:00.000Z', value: 314.2 },
  ]);
  g.done();
});

test('guide 2 — invoice and advance: upload into Documents, book it on Costs, recurring advances, split per house', async ({ page }) => {
  const g = guide(page, 'faktura');
  const component = {
    id: 'c-el', name: 'Elektřina – vodárna', code: 'ELEKTRINA_VODARNA', startDate: '2023-11-01',
    allocationBasis: 'CostEntries', waterRole: 'None', active: true, note: null, currentMethod: 'Equal', currentParticipants: 4,
  };
  const invoice = {
    id: 'doc-1', category: 'faktury', name: 'Faktura elektřina 3Q 2026', fileSizeBytes: 84_000, contentType: 'application/pdf',
    uploadedAt: '2026-09-25T08:00:00Z', uploadedBy: 'u-admin', componentId: 'c-el',
  };
  const uploads: string[] = [];
  const entries: Record<string, unknown>[] = [];
  let booked: Record<string, unknown> | null = null;
  let recurring: Record<string, unknown> | null = null;
  const shares = houses.map((h) => ({ houseId: h.id, houseName: h.name, weight: 1, amount: 1062.5 }));
  await mockApi(page, {
    ...signIn,
    '/cost-components': () => [component],
    '/documents': (request) => {
      if (request.method() === 'POST') {
        uploads.push(request.url());
        return invoice;
      }
      return uploads.length > 0 ? [invoice] : [];
    },
    '/documents/unaccounted': () => (booked ? [] : [{ document: invoice, componentName: component.name }]),
    '/cost-components/c-el/entries': (request) => {
      if (request.method() === 'POST') {
        booked = request.postDataJSON();
        const entry = {
          id: 'e-inv', componentId: 'c-el', componentName: component.name, type: 'OneOff', periodFrom: '2026-07-01', periodTo: '2026-09-30',
          days: 92, amount: 4250, quantityM3: null, supplier: 'Energie Ukázka', documentId: 'doc-1', paidFrom: 'Bank', note: null, locked: false,
        };
        entries.push(entry);
        return entry;
      }
      return entries;
    },
    '/cost-components/c-el/entries/recurring': (request) => {
      recurring = request.postDataJSON();
      return Array.from({ length: 12 }, (_, i) => ({ id: `r${i}` }));
    },
    '/cost-components/c-el/entries/e-inv/allocation': () => ({
      entry: entries[0],
      segments: [{ from: '2026-07-01', to: '2026-09-30', days: 92, method: 'Equal', amount: 4250, shares }],
      houseTotals: shares,
    }),
  });

  await signInAsAdmin(page);
  await navigate(page, '/documents');
  await page.getByText('Nahrát faktury a vyúčtování (více souborů najednou)').click();
  const bulk = page.getByLabel('Hromadné nahrání faktur');
  await bulk.locator('input[type=file]').setInputFiles({ name: 'Faktura elektřina 3Q 2026.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4') });
  await bulk.getByLabel('Nákladová složka (nepovinné)').selectOption({ label: 'Elektřina – vodárna' });
  await g.shot(1, page.locator('details', { hasText: 'Nahrát faktury a vyúčtování' }));
  await bulk.getByRole('button', { name: 'Nahrát 1' }).click();
  await expect(page.getByLabel('Výsledek nahrání')).toContainText('nahráno');
  expect(uploads).toHaveLength(1);
  expect(uploads[0]).toContain('category=faktury');
  expect(uploads[0]).toContain('componentId=c-el');

  await navigate(page, '/naklady');
  const unaccounted = page.getByRole('region', { name: 'Dokumenty bez zaúčtování' });
  await expect(unaccounted).toContainText('Faktura elektřina 3Q 2026');
  await g.shot(2, unaccounted);
  await unaccounted.getByRole('button', { name: 'Zaúčtovat' }).click();

  const form = page.getByRole('form', { name: 'Přidat náklad' });
  await expect(form.getByLabel('Doklad (PDF v Dokumentech)')).toHaveValue('doc-1');
  await form.getByLabel('Druh').selectOption({ label: 'Jednorázový / faktura' });
  await form.getByLabel('Období od').fill('2026-07-01');
  await form.getByLabel('do', { exact: true }).fill('2026-09-30');
  await form.getByLabel('Částka (Kč)').fill('4 250');
  await form.getByLabel('Dodavatel', { exact: true }).fill('Energie Ukázka');
  await form.getByLabel('Úhrada').selectOption({ label: 'Z účtu' });
  await g.shot(3, form);
  await form.getByRole('button', { name: 'Přidat' }).click();

  await expect.poll(() => booked).not.toBeNull();
  expect(booked).toMatchObject({
    type: 'OneOff', periodFrom: '2026-07-01', periodTo: '2026-09-30', amount: 4250, supplier: 'Energie Ukázka', paidFrom: 'Bank', documentId: 'doc-1',
  });
  const table = page.getByRole('table', { name: 'Nákladové záznamy' });
  await expect(table).toContainText('4 250,00 Kč');
  await expect(page.getByRole('region', { name: 'Dokumenty bez zaúčtování' })).toHaveCount(0);

  const advance = page.getByRole('form', { name: 'Opakovaná záloha' });
  await advance.getByLabel('Částka (Kč)').fill('1 400');
  await advance.getByLabel('Perioda').selectOption({ label: 'měsíčně' });
  await advance.getByLabel('Od', { exact: true }).fill('2026-10-01');
  await advance.getByLabel('Do', { exact: true }).fill('2027-09-30');
  await advance.getByLabel('Dodavatel', { exact: true }).fill('Energie Ukázka');
  await advance.getByRole('button', { name: 'Vytvořit zálohy' }).click();
  await expect(advance.getByRole('status')).toHaveText('Vytvořeno 12 záloh.');
  await g.shot(4, advance);
  expect(recurring).toMatchObject({ amount: 1400, periodicity: 'Monthly', from: '2026-10-01', to: '2027-09-30', supplier: 'Energie Ukázka', paidFrom: 'Bank' });

  await table.getByRole('button', { name: 'Rozpad na domy' }).click();
  await expect(page.getByRole('table', { name: 'Součty za domy' }).getByRole('row')).toHaveCount(4);
  await g.shot(5, table);
  g.done();
});

test('guide 3 — cash book: an expense without a receipt as a shared cost, then its storno', async ({ page }) => {
  const g = guide(page, 'pokladna');
  const entry = (over: Record<string, unknown>) => ({
    id: 'x', date: '2026-09-01', type: 'Deposit', amount: 0, effect: 0, balance: 0, category: 'výběr z účtu', description: '', counterparty: null,
    hasReceipt: true, documentId: null, bankTransactionRef: null, correctionOf: null, correctedBy: null, componentId: null, componentName: null,
    costEntryId: null, createdByName: 'Alena Ukázková', ...over,
  });
  const book = {
    from: null, to: null, openingBalance: 0, deposits: 5000, expenses: 350, closingBalance: 4650,
    entries: [
      entry({ id: 'd1', amount: 5000, effect: 5000, balance: 5000, description: 'Výběr z účtu do pokladny', bankTransactionRef: 'výpis 9/2026' }),
      entry({ id: 'e1', date: '2026-09-05', type: 'Expense', amount: 350, effect: -350, balance: 4650, category: 'materiál', description: 'Pytle na trávu', counterparty: 'Zahradnictví Ukázka' }),
    ],
  };
  let created: Record<string, unknown> | null = null;
  let storno: Record<string, unknown> | null = null;
  await mockApi(page, {
    ...signIn,
    '/documents': () => [],
    '/cost-components': () => [{
      id: 'c-okoli', name: 'Údržba okolí', code: 'OKOLI', startDate: '2023-11-01', allocationBasis: 'CostEntries', waterRole: 'None',
      active: true, note: null, currentMethod: 'Equal', currentParticipants: 4,
    }],
    '/cash-book': (request) => {
      if (request.method() === 'POST') {
        created = request.postDataJSON();
        const added = entry({
          id: 'e2', date: '2026-09-20', type: 'Expense', amount: 1200, effect: -1200, balance: 3450, category: 'údržba okolí',
          description: 'Sekání trávy u vodárny', counterparty: 'Karel Vzorový', hasReceipt: false, componentId: 'c-okoli', componentName: 'Údržba okolí',
        });
        book.entries.push(added);
        book.expenses = 1550;
        book.closingBalance = 3450;
        return added;
      }
      return book;
    },
    '/cash-book/e2/storno': (request) => {
      storno = request.postDataJSON();
      return entry({ id: 's1', type: 'Correction' });
    },
  });

  await signInAsAdmin(page);
  await navigate(page, '/pokladna');
  await expect(page.getByRole('heading', { name: 'Pokladna' })).toBeVisible();
  await expect(page.getByText('4 650,00 Kč').first()).toBeVisible();
  await g.shot(1);

  const form = page.getByRole('form', { name: 'Nový záznam pokladny' });
  await expect(form.getByLabel('Druh')).toHaveValue('Expense');
  await form.getByLabel('Datum').fill('2026-09-20');
  await form.getByLabel('Částka (Kč)').fill('1 200');
  await form.getByLabel('Kategorie').fill('údržba okolí');
  await form.getByLabel('Co', { exact: true }).fill('Sekání trávy u vodárny');
  await form.getByLabel('Komu').fill('Karel Vzorový');
  await form.getByLabel('mám doklad').uncheck();
  await form.getByLabel('Společný náklad složky').selectOption({ label: 'Údržba okolí' });
  await expect(form.getByText('Výdaj bez dokladu: vyplňte, co se zaplatilo a komu.')).toBeVisible();
  await g.shot(2, form);
  await form.getByRole('button', { name: 'Zapsat' }).click();

  await expect.poll(() => created).not.toBeNull();
  expect(created).toEqual({
    date: '2026-09-20', type: 'Expense', amount: 1200, category: 'údržba okolí', description: 'Sekání trávy u vodárny',
    counterparty: 'Karel Vzorový', hasReceipt: false, componentId: 'c-okoli',
  });
  const cashBook = page.getByRole('region', { name: 'Pokladní kniha' });
  const row = cashBook.getByRole('row', { name: /Sekání trávy u vodárny/ });
  await expect(row.getByLabel('bez dokladu')).toBeVisible();
  await expect(cashBook).toContainText('3 450,00 Kč');
  await g.shot(3, cashBook);

  await row.getByRole('button', { name: 'Storno' }).click();
  await cashBook.getByLabel('Důvod storna').fill('zapsáno omylem, zaplaceno z účtu');
  const stornoButton = cashBook.getByRole('button', { name: /Stornovat 1\s200,00\sKč/ });
  await expect(stornoButton).toBeEnabled();
  await g.shot(4, cashBook);
  await stornoButton.click();
  await expect.poll(() => storno).toEqual({ reason: 'zapsáno omylem, zaplaceno z účtu' });
  g.done();
});

test('guide 4 — interim closing: annual closing of all houses and its snapshot', async ({ page }) => {
  const g = guide(page, 'mezizaverka');
  const closing = {
    id: '2026-12-31|All|-', date: '2026-12-31', scope: 'All', houseId: null, houseName: null, reason: 'roční závěrka 2026',
    createdByName: 'Alena Ukázková', createdAt: '2027-01-05T08:00:00Z', totalSaldo: 1240, canDelete: true, houses: null,
  };
  const snapshot = [
    { houseId: 'A', houseName: 'RD A', opening: 0, payments: 18000, costs: 17420, saldo: 580, currentSaldo: 580, difference: 0 },
    { houseId: 'B', houseName: 'RD B', opening: 200, payments: 18000, costs: 18650, saldo: -450, currentSaldo: -450, difference: 0 },
    { houseId: 'C', houseName: 'RD C', opening: 0, payments: 18000, costs: 17110, saldo: 890, currentSaldo: 890, difference: 0 },
    { houseId: 'D', houseName: 'RD D', opening: 0, payments: 18000, costs: 17780, saldo: 220, currentSaldo: 220, difference: 0 },
  ];
  let created: Record<string, unknown> | null = null;
  await mockApi(page, {
    ...signIn,
    '/houses': () => houses,
    '/interim-closings': (request) => {
      if (request.method() === 'POST') {
        created = request.postDataJSON();
        return closing;
      }
      return created ? [closing] : [];
    },
    '/interim-closings/2026-12-31%7CAll%7C-': () => ({ ...closing, houses: snapshot }),
  });

  await signInAsAdmin(page);
  await navigate(page, '/mezizaverky');
  await expect(page.getByRole('heading', { name: 'Mezizávěrky' })).toBeVisible();

  const form = page.getByRole('form', { name: 'Nová mezizávěrka' });
  await form.getByLabel('Uzavřít do (včetně)').fill('2026-12-31');
  await expect(form.getByLabel('Rozsah')).toHaveValue('All');
  await form.getByLabel('Důvod').fill('roční závěrka 2026');
  await g.shot(2, form);
  await form.getByRole('button', { name: 'Uzavřít' }).click();

  await expect.poll(() => created).not.toBeNull();
  expect(created).toEqual({ date: '2026-12-31', scope: 'All', reason: 'roční závěrka 2026' });
  const detail = page.getByRole('region', { name: 'Mezizávěrka k 31. 12. 2026' });
  await expect(detail.getByText('Saldo k datu mezizávěrky odpovídá snímku.')).toBeVisible();
  await expect(detail.getByRole('table', { name: 'Snímek salda' }).getByRole('row')).toHaveCount(5);
  await g.shot(3, detail);

  // Steps 4 and 5: the export for the accountant and the cancellation (needs a reason).
  await expect(detail.getByRole('button', { name: 'Export pro účetní (XLSX)' })).toBeVisible();
  await expect(detail.getByRole('button', { name: 'Zrušit mezizávěrku' })).toBeDisabled();
  await detail.getByLabel('Důvod zrušení').fill('uzavřeno omylem');
  await expect(detail.getByRole('button', { name: 'Zrušit mezizávěrku' })).toBeEnabled();
  g.done();
});

test('guide 5 — house transfer: impact preview, new owner with the handover meter state', async ({ page }) => {
  const g = guide(page, 'prevod');
  let transfer: Record<string, unknown> | null = null;
  await mockApi(page, {
    ...signIn,
    '/houses': () => houses,
    '/meters': () => [],
    '/cost-components': () => [],
    '/ownership-periods': () => houses.map((h) => ({
      id: `${h.id}|2023-11-01`, houseId: h.id, houseName: h.name, ownerName: h.id === 'B' ? 'Alena Ukázková' : h.contactPerson,
      contact: null, validFrom: '2023-11-01', validTo: null,
    })),
    '/opening-balances': () => [],
    '/houses/B/transfer-preview': () => ({
      houseId: 'B', houseName: 'RD B', transferDate: '2026-10-01', closingDate: '2026-09-30', currentOwnerName: 'Alena Ukázková', currentOwnerFrom: '2023-11-01',
      closingSaldo: 640, closingPayments: 52000, closingCosts: 51360, meterId: 'mB', meterNumber: 'V-B',
      suggestedMeterValue: 289.412, suggestedMeterNote: 'Odhad lineární interpolací mezi odečty 1. 9. 2026 a 1. 10. 2026.', problems: [],
    }),
    '/houses/B/transfer': (request) => {
      transfer = request.postDataJSON();
      return { closingId: '2026-09-30|House|B', closingSaldo: 640, newOwnershipPeriodId: 'B|2026-10-01' };
    },
  });

  await signInAsAdmin(page);
  await navigate(page, '/admin/opening-balances');

  const section = page.getByRole('region', { name: 'Převod domu' });
  await expect(section.getByRole('heading', { name: 'Převod domu na nového majitele' })).toBeVisible();
  await section.getByRole('combobox').first().selectOption({ label: 'RD B' });
  await section.getByLabel('Nový vlastník od').fill('2026-10-01');
  await g.shot(1, section);
  await section.getByRole('button', { name: 'Náhled dopadů' }).click();

  const preview = section.getByLabel('Náhled převodu');
  await expect(preview).toContainText('Alena Ukázková');
  await expect(preview).toContainText('30. 9. 2026');
  await expect(preview).toContainText('640,00 Kč přeplatek');
  await g.shot(2, section);

  const form = section.getByRole('form', { name: 'Údaje nového vlastníka' });
  await expect(form.getByLabel('Stav vodoměru V-B (m³)')).toHaveValue('289,412');
  await form.getByLabel('Nový vlastník', { exact: true }).fill('Bohumil Vzorný');
  await form.getByLabel('Kontakt (e-mail)').fill('bohumil@example.cz');
  await form.getByLabel('Stav vodoměru V-B (m³)').fill('289,5');
  await form.getByLabel('Zdroj stavu').fill('předávací protokol');
  await expect(form.getByLabel('Podíl ve fondu (Kč)')).toHaveValue('0');
  await expect(form.getByLabel('změnit kontakt domu')).toBeChecked();
  await g.shot(3, section);
  await form.getByRole('button', { name: 'Převést dům' }).click();

  await expect(section.getByRole('status')).toContainText('Dům převeden. Závěrečné saldo původního vlastníka: 640,00 Kč (přeplatek)');
  expect(transfer).toMatchObject({
    transferDate: '2026-10-01', newOwnerName: 'Bohumil Vzorný', newOwnerContact: 'bohumil@example.cz', meterValue: 289.5,
    meterIsEstimate: false, meterSource: 'předávací protokol', fundShare: 0, updateHouseContact: true,
  });
  await g.shot(4, section);
  g.done();
});

test('guide page: sidebar entry, table of contents, numbered steps, page links and screenshots from the content', async ({ page }) => {
  await mockApi(page, signIn);
  await signInAsAdmin(page);

  await page.getByRole('link', { name: 'Návod pro správce' }).click();
  await expect(page.getByRole('heading', { name: 'Návod pro správce', level: 1 })).toBeVisible();

  const toc = page.getByRole('navigation', { name: 'Obsah návodu' });
  await expect(toc.getByRole('link')).toHaveText(adminProcedures.map((p) => p.title));

  for (const [i, p] of adminProcedures.entries()) {
    const section = page.getByRole('region', { name: `${i + 1}. ${p.title}` });
    await expect(section.getByRole('link', { name: 'Otevřít stránku' })).toHaveAttribute('href', p.path);
    await expect(section.getByRole('listitem')).toHaveCount(p.steps.length);
    // While generating, the images are not in the build yet (a missing one hides itself), so skip the check.
    if (!SAVE_SCREENSHOTS) {
      const files = p.steps.flatMap((s) => (s.screenshot ? [`/navod/${s.screenshot}`] : []));
      const sources = await section.locator('img').evaluateAll((imgs) => imgs.map((img) => img.getAttribute('src')));
      expect(sources).toEqual(files);
    }
  }
});
