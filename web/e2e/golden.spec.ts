import { readFileSync } from 'node:fs';
import { expect, test } from '@playwright/test';
import type { Page, Request } from '@playwright/test';
import type { CashBook, CashBookEntry } from '../src/api/cashBook';
import type { CostComponent } from '../src/api/costComponents';
import type { CostEntry, CostEntryAllocation } from '../src/api/costEntries';
import type { HouseTransferPreview } from '../src/api/houseTransfer';
import type { HouseLedger, LedgerItemKind, LedgerOverview } from '../src/api/ledger';
import type { OffBookFund, OffBookFundDetail } from '../src/api/offBookFunds';
import type { OpeningBalance, OwnershipPeriod } from '../src/api/openingBalances';
import type { ReadingEstimate } from '../src/api/readings';
import type { WaterSettlement } from '../src/api/waterSettlement';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';
import type { Handler } from './mockApi';

/**
 * T14 regression scenarios S1–S8 (docs/ZADANI.md §4) through the UI. The mocked API serves the golden fixtures in
 * `e2e/golden/` — the JSON the real backend produces for each scenario, generated and checked by
 * `api/tests/Oaza.Functions.Tests/Golden/GoldenFixturesTests.cs` (regenerate: `UPDATE_GOLDEN=1 dotnet test`).
 * So these tests check real calculations as the user sees them, in Czech formatting. All data is fictitious.
 */

const golden = <T>(name: string): T =>
  JSON.parse(readFileSync(new URL(`./golden/${name}.json`, import.meta.url), 'utf8')) as T;

/** Czech amount as the UI prints it (`1 000,00 Kč`), tolerant to the (narrow) no-break spaces and minus signs of Intl. */
const kc = (text: string) =>
  new RegExp(text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace(/-/g, '[-−]').replace(/ /g, '\\s*') + '\\s*Kč');

const base: Record<string, Handler> = {
  '/environment': () => ({ environment: 'test' }),
  '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
  '/auth/me': () => adminUser,
};

const query = (request: Request) => new URL(request.url()).searchParams;

async function open(page: Page, handlers: Record<string, Handler>, path: string): Promise<void> {
  await mockApi(page, { ...base, ...handlers });
  await signInAsAdmin(page);
  await navigate(page, path);
}

// ───────────────────── S1 – water losses ─────────────────────

const s1 = [
  { fixture: 'S1-water-equal', method: 'rovným dílem', losses: ['250,00', '250,00', '250,00', '250,00'] },
  { fixture: 'S1-water-ratio', method: 'poměrem', losses: ['444,44', '333,33', '166,67', '55,56'] },
];

for (const { fixture, method, losses } of s1) {
  test(`S1 water losses (${fixture}): 10 m³ loss of 1 000,00 Kč split ${method}`, async ({ page }) => {
    const settlement = golden<WaterSettlement>(fixture);
    const ranges: string[] = [];
    await open(page, {
      '/water-settlement': (request) => { ranges.push(`${query(request).get('from')}..${query(request).get('to')}`); return settlement; },
    }, '/voda');
    await expect(page.getByRole('heading', { name: 'Voda a ztráty' })).toBeVisible();

    await page.getByLabel('Od', { exact: true }).fill('2026-01-01');
    await page.getByLabel('Do', { exact: true }).fill('2026-06-30');
    await page.getByRole('button', { name: 'Zobrazit' }).click();
    await expect.poll(() => ranges.at(-1)).toBe('2026-01-01..2026-06-30');

    const interval = page.getByRole('region', { name: 'Úsek 1. 1. 2026 – 30. 6. 2026' });
    await expect(interval).toContainText(/Hlavní vodoměr\s*100 m³/);
    await expect(interval).toContainText(/Domy celkem\s*90 m³/);
    await expect(interval).toContainText(/Ztráta\s*10 m³/);
    await expect(interval).toContainText(/Cena za m³\s*100,00\s*Kč/);
    await expect(interval).toContainText(/Ztráty\s*1\s*000,00\s*Kč/);
    await expect(interval.getByText(new RegExp(`${method} mezi 4 domů`))).toBeVisible();
    for (const [i, house] of ['RD A', 'RD B', 'RD C', 'RD D'].entries()) {
      const row = interval.getByRole('row', { name: new RegExp(house) });
      await expect(row.getByRole('cell').nth(3)).toHaveText(kc(losses[i]));
    }
  });
}

// ───────────────────── S2 – waterworks credit ─────────────────────

interface S2 { overview: LedgerOverview; houseA: HouseLedger; houseE: HouseLedger }

test('S2 waterworks credit: house A has a 5 000,00 Kč credit and 125,00 Kč advance cost each month, house E nothing', async ({ page }) => {
  const s2 = golden<S2>('S2-ledger');
  const overviewRanges: string[] = [];
  const ledgerRanges: string[] = [];
  await open(page, {
    '/ledger/overview': (request) => { overviewRanges.push(`${query(request).get('from')}..${query(request).get('to')}`); return s2.overview; },
    '/ledger/houses/A': (request) => { ledgerRanges.push(`${query(request).get('from')}..${query(request).get('to')}`); return s2.houseA; },
    '/ledger/houses/E': () => s2.houseE,
  }, '/saldo-domu');

  await page.getByLabel('Od (prázdné = start účtování)').fill('2023-11-01');
  await page.getByLabel('Do (prázdné = dnes)').fill('2023-12-31');
  await page.getByRole('button', { name: 'Zobrazit' }).click();
  await expect.poll(() => overviewRanges.at(-1)).toBe('2023-11-01..2023-12-31');
  await expect(page.getByRole('heading', { name: 'Všechny domy 1. 11. 2023 – 31. 12. 2023' })).toBeVisible();

  const table = page.getByRole('table', { name: 'Saldo domů' });
  await expect(table.getByRole('row', { name: /RD A/ })).toContainText(/4\s*750,00\s*Kč přeplatek/);
  await expect(table.getByRole('row', { name: /RD E/ })).toContainText('vyrovnáno');
  await expect(table.getByRole('row', { name: /RD F/ })).toContainText('vyrovnáno');

  await table.getByRole('row', { name: /RD A/ }).click();
  await expect.poll(() => ledgerRanges.at(-1)).toBe('2023-11-01..2023-12-31');
  const detail = page.getByRole('region', { name: 'Saldo RD A' });
  const items = detail.getByRole('table', { name: 'Položky salda' });
  await expect(items.getByRole('row')).toHaveCount(4); // header + credit + two monthly advances
  await expect(items.getByRole('row', { name: /Kredit u dodavatele/ })).toContainText(kc('+5 000,00'));
  const advances = items.getByRole('row', { name: /Záloha/ });
  await expect(advances).toHaveCount(2);
  for (const row of await advances.all()) await expect(row.getByRole('cell').nth(2)).toHaveText(kc('-125,00'));
  const credit = items.getByRole('row', { name: /Kredit u dodavatele/ });
  await credit.getByText('Jak vznikl').click();
  await expect(credit).toContainText(/Kredit -20\s*000,00\s*Kč .*rovným dílem mezi 4 domů: podíl 1 z 4 = -5\s*000,00\s*Kč/);

  await table.getByRole('row', { name: /RD E/ }).click();
  const e = page.getByRole('region', { name: 'Saldo RD E' });
  await expect(e.getByText('V období nejsou žádné položky.')).toBeVisible();
  await expect(e).toContainText(/Saldo\s*vyrovnáno/);
});

// ───────────────────── S3 – joining house ─────────────────────

test('S3 joining house: Osvětlení A–D 414,00 Kč, E 184,00 Kč, the control row matches 1 840,00 Kč', async ({ page }) => {
  const { overview } = golden<{ overview: LedgerOverview }>('S3-ledger');
  const ranges: string[] = [];
  await open(page, {
    '/ledger/overview': (request) => { ranges.push(`${query(request).get('from')}..${query(request).get('to')}`); return overview; },
  }, '/saldo-domu');

  await page.getByLabel('Od (prázdné = start účtování)').fill('2026-07-01');
  await page.getByLabel('Do (prázdné = dnes)').fill('2026-12-31');
  await page.getByRole('button', { name: 'Zobrazit' }).click();
  await expect.poll(() => ranges.at(-1)).toBe('2026-07-01..2026-12-31');
  await expect(page.getByRole('heading', { name: 'Všechny domy 1. 7. 2026 – 31. 12. 2026' })).toBeVisible();

  const table = page.getByRole('table', { name: 'Saldo domů' });
  await expect(table.getByRole('columnheader', { name: 'Osvětlení' })).toBeVisible();
  const expected: [string, string][] = [['RD A', '414,00'], ['RD B', '414,00'], ['RD C', '414,00'], ['RD D', '414,00'], ['RD E', '184,00'], ['RD F', '0,00']];
  for (const [house, amount] of expected) {
    await expect(table.getByRole('row', { name: new RegExp(house) }).getByRole('cell').nth(2)).toHaveText(kc(amount));
  }
  await expect(table.getByRole('row', { name: /RD E/ })).toContainText(/184,00\s*Kč nedoplatek/);
  const control = table.getByRole('row', { name: /Kontrola/ });
  await expect(control).toContainText(/✓ 1\s*840,00\s*Kč/);
  await expect(control.getByTitle(/Rozpočteno 1\s*840,00\s*Kč, Σ domů 1\s*840,00\s*Kč/)).toBeVisible();
});

// ───────────────────── S4 – house transfer ─────────────────────

interface S4 {
  periodsBefore: OwnershipPeriod[];
  balancesBefore: OpeningBalance[];
  preview: HouseTransferPreview;
  transfer: { closingId: string; closingSaldo: number; newOwnershipPeriodId: string };
  periodsAfter: OwnershipPeriod[];
  balancesAfter: OpeningBalance[];
  overview: LedgerOverview;
  ledgerNew: HouseLedger;
  ledgerOld: HouseLedger;
}

test('S4 house transfer: B sold on 15. 3. — old owner closed at 14. 3., the new owner starts with meter 1 234,567 m³ and fund 0', async ({ page }) => {
  const s4 = golden<S4>('S4-house-transfer');
  let transferred = false;
  let body: Record<string, unknown> | null = null;
  const houses = ['A', 'B', 'C', 'D'].map((h) => ({ id: h, name: `RD ${h}`, address: '', contactPerson: `Vlastník ${h}`, email: '', isActive: true, dissolveOverpayment: false }));
  await open(page, {
    '/houses': () => houses,
    '/meters': () => [{ id: 'mB', meterNumber: 'V-B', name: '', type: 'Individual', houseId: 'B', houseName: 'RD B', radioAddress: null, installationDate: '2020-01-01T00:00:00Z' }],
    '/cost-components': () => [],
    '/ownership-periods': () => (transferred ? s4.periodsAfter : s4.periodsBefore),
    '/opening-balances': () => (transferred ? s4.balancesAfter : s4.balancesBefore),
    '/houses/B/transfer-preview': (request) => (query(request).get('date') === '2026-03-15' ? s4.preview : { ...s4.preview, problems: ['nečekané datum'] }),
    '/houses/B/transfer': (request) => { body = request.postDataJSON(); transferred = true; return s4.transfer; },
    '/ledger/overview': () => s4.overview,
    '/ledger/houses/B': (request) => (query(request).get('ownershipPeriodId') === 'B|2023-11-01' ? s4.ledgerOld : s4.ledgerNew),
  }, '/admin/opening-balances');

  const section = page.getByRole('region', { name: 'Převod domu' });
  await section.getByRole('combobox').first().selectOption({ label: 'RD B' });
  await section.getByLabel('Nový vlastník od').fill('2026-03-15');
  await section.getByRole('button', { name: 'Náhled dopadů' }).click();

  const preview = section.getByLabel('Náhled převodu');
  await expect(preview).toContainText(/Uzavře se k\s*14\. 3\. 2026/);
  await expect(preview).toContainText(/1\s*000,00\s*Kč \/ 350,00\s*Kč/);
  await expect(preview).toContainText(/650,00\s*Kč přeplatek/);
  const form = section.getByRole('form', { name: 'Údaje nového vlastníka' });
  await expect(form.getByLabel('Stav vodoměru V-B (m³)')).toHaveValue('1234,516'); // interpolated suggestion
  await form.getByLabel('Stav vodoměru V-B (m³)').fill('1234,567');
  await form.getByLabel('Nový vlastník', { exact: true }).fill('Noví vlastníci B');
  await form.getByLabel('Kontakt (e-mail)').fill('novi.b@example.cz');
  await form.getByLabel('Zdroj stavu').fill('předávací protokol');
  await expect(form.getByLabel('Podíl ve fondu (Kč)')).toHaveValue('0');
  await form.getByRole('button', { name: 'Převést dům' }).click();

  await expect(section.getByRole('status')).toHaveText(/Závěrečné saldo původního vlastníka: 650,00\s*Kč \(přeplatek\)/);
  expect(body).toMatchObject({ transferDate: '2026-03-15', newOwnerName: 'Noví vlastníci B', meterValue: 1234.567, fundShare: 0 });

  // The new owner's opening values as the wizard shows them after the transfer.
  const meters = page.getByRole('region', { name: 'Stavy vodoměrů' });
  await expect(meters.getByLabel('Hodnota RD B')).toHaveValue('1234,567');
  await expect(meters).toContainText('k 15. 3. 2026');
  const fund = page.getByRole('region', { name: 'Podíl ve fondu spolku' });
  await expect(fund.getByLabel('Hodnota RD B')).toHaveValue('0');

  // Ledger: costs from 15. 3. on the new ownership period, the old one closes with its saldo.
  await navigate(page, '/saldo-domu');
  await page.getByRole('table', { name: 'Saldo domů' }).getByRole('row', { name: /RD B/ }).click();
  const detail = page.getByRole('region', { name: 'Saldo RD B' });
  await expect(detail).toContainText('15. 3. 2026 – 10. 4. 2026 · vlastník Noví vlastníci B');
  await expect(detail).toContainText(/Počáteční podíl\s*0,00\s*Kč/);
  await expect(detail).toContainText(/Náklady\s*425,00\s*Kč/);
  await detail.getByLabel('Období vlastnictví').selectOption({ label: 'Vlastník B (1. 11. 2023 – 14. 3. 2026)' });
  await expect(detail).toContainText('1. 11. 2023 – 14. 3. 2026 · vlastník Vlastník B');
  await expect(detail).toContainText(/Platby\s*1\s*000,00\s*Kč/);
  await expect(detail).toContainText(/Náklady\s*350,00\s*Kč/);
  await expect(detail).toContainText(/Saldo\s*650,00\s*Kč přeplatek/);
});

// ───────────────────── S5 – cash book ─────────────────────

interface S5 {
  requests: Record<'deposit' | 'expense' | 'rejected', Record<string, unknown>> & { storno: { reason: string } };
  initial: CashBook;
  deposit: CashBookEntry;
  afterDeposit: CashBook;
  expense: CashBookEntry;
  afterExpense: CashBook;
  rejected: { error: string; errors: { field: string; message: string }[] };
  storno: CashBookEntry;
  afterStorno: CashBook;
}

test('S5 cash book: deposit 5 000, expense 2 000 → 3 000, expense 4 000 rejected, storno → 5 000', async ({ page }) => {
  const s5 = golden<S5>('S5-cash-book');
  const books = [s5.initial, s5.afterDeposit, s5.afterExpense, s5.afterStorno];
  let step = 0;
  const posted: Record<string, unknown>[] = [];
  let stornoBody: unknown = null;
  await mockApi(page, { ...base, '/cost-components': () => [], '/documents': () => [] });
  // The cash book answers like the API: 201 for an accepted entry, 400 { error, errors } for the rejected one.
  await page.route('**/api/cash-book**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const json = (status: number, body: unknown) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (request.method() === 'GET' && path === '/api/cash-book') return json(200, books[step]);
    if (request.method() === 'POST' && path === '/api/cash-book') {
      const body = request.postDataJSON() as Record<string, unknown>;
      posted.push(body);
      if (body.amount === 5000) { step = 1; return json(201, s5.deposit); }
      if (body.amount === 2000) { step = 2; return json(201, s5.expense); }
      return json(400, s5.rejected);
    }
    if (request.method() === 'POST' && path === `/api/cash-book/${s5.expense.id}/storno`) {
      stornoBody = request.postDataJSON();
      step = 3;
      return json(201, s5.storno);
    }
    return route.fallback();
  });
  await signInAsAdmin(page);
  await navigate(page, '/pokladna');

  const book = page.getByRole('region', { name: 'Pokladní kniha' });
  const table = page.getByRole('table', { name: 'Záznamy pokladny' });
  const form = page.getByRole('form', { name: 'Nový záznam pokladny' });
  await expect(table.getByText('V období nejsou žádné záznamy.')).toBeVisible();

  // Deposit 5 000 (cash withdrawn from the account).
  await form.getByLabel('Druh').selectOption('Deposit');
  await form.getByLabel('Datum').fill('2026-09-01');
  await form.getByLabel('Částka (Kč)').fill('5000');
  await form.getByLabel('Co', { exact: true }).fill('Výběr z účtu');
  await form.getByLabel('Pohyb v bance').fill('výpis 9/2026');
  await form.getByRole('button', { name: 'Zapsat' }).click();
  await expect(book).toContainText(/Zůstatek v pokladně\s*5\s*000,00\s*Kč/);

  // Expense 2 000 without a receipt → balance 3 000.
  await form.getByLabel('Druh').selectOption('Expense');
  await form.getByLabel('Datum').fill('2026-09-02');
  await form.getByLabel('Částka (Kč)').fill('2000');
  await form.getByLabel('Co', { exact: true }).fill('úprava okolí');
  await form.getByLabel('Komu').fill('Zahradník Vzorový');
  await form.getByLabel('mám doklad').uncheck();
  await form.getByRole('button', { name: 'Zapsat' }).click();
  await expect(book).toContainText(/Zůstatek v pokladně\s*3\s*000,00\s*Kč/);
  await expect(table.getByRole('row', { name: /úprava okolí/ }).getByLabel('bez dokladu')).toBeVisible();

  // Expense 4 000 → rejected with the backend's reason, the balance stays 3 000.
  await form.getByLabel('Datum').fill('2026-09-03');
  await form.getByLabel('Částka (Kč)').fill('4000');
  await form.getByLabel('Co', { exact: true }).fill('další úprava okolí');
  await form.getByLabel('Komu').fill('Zahradník Vzorový');
  await form.getByRole('button', { name: 'Zapsat' }).click();
  await expect(form.getByRole('alert')).toHaveText(s5.rejected.errors[0].message);
  await expect(form.getByRole('alert')).toContainText(/záporný zůstatek pokladny \(-1\s*000,00\s*Kč k 3\. 9\. 2026\)/);
  await expect(book).toContainText(/Zůstatek v pokladně\s*3\s*000,00\s*Kč/);

  expect(posted).toHaveLength(3);
  for (const [i, key] of (['deposit', 'expense', 'rejected'] as const).entries()) {
    const sent = s5.requests[key];
    expect(posted[i]).toMatchObject({ date: sent.date, type: sent.type, amount: sent.amount, category: sent.category, description: sent.description, hasReceipt: sent.hasReceipt });
  }

  // Storno of the 2 000 expense → balance 5 000.
  await table.getByRole('row', { name: /úprava okolí/ }).getByRole('button', { name: 'Storno' }).click();
  await table.getByLabel('Důvod storna').fill(s5.requests.storno.reason);
  await table.getByRole('button', { name: /Stornovat 2\s*000,00\s*Kč/ }).click();
  await expect(book).toContainText(/Zůstatek v pokladně\s*5\s*000,00\s*Kč/);
  expect(stornoBody).toEqual(s5.requests.storno);
});

// ───────────────────── S6 – off-book fund ─────────────────────

interface S6 { features: { offBookFund: boolean }; funds: OffBookFund[]; detail: OffBookFundDetail }

test('S6 off-book fund: call 1 230 Kč × 6 houses, 5 paid → 1 debtor, balance 6 150; no fund items in any ledger', async ({ page }) => {
  const s6 = golden<S6>('S6-off-book-fund');
  const fundId = s6.funds[0].id;
  await open(page, {
    '/features': () => s6.features,
    '/houses': () => [],
    '/off-book-funds': () => s6.funds,
    [`/off-book-funds/${fundId}`]: () => s6.detail,
  }, '/dashboard');

  await page.getByRole('link', { name: 'Hospodaření' }).click();
  await page.getByRole('link', { name: 'Oddělený fond' }).click();
  await expect(page.getByRole('note')).toHaveText('Fond mimo účetnictví spolku – peníze nejsou na účtu spolku.');
  const fund = page.getByRole('region', { name: 'Fond Fond na ohňostroje' });
  await expect(fund).toContainText(/Zůstatek fondu\s*6\s*150,00\s*Kč/);
  const call = fund.getByLabel('Výzva Silvestr 2026');
  await expect(call).toContainText(/1\s*230,00\s*Kč za dům do 15\. 12\. 2026/);
  await expect(call).toContainText(/Vybráno 6\s*150,00\s*Kč · nezaplatil 1 dům/);
  await expect(call.getByRole('listitem')).toHaveCount(6);
  await expect(call.getByRole('listitem').filter({ hasText: 'chybí' })).toHaveText([/RD F: chybí 1\s*230,00\s*Kč/]);

  // Isolation (T10): the ledger fixtures of S2–S4 carry only ledger item kinds and nothing of the fund.
  const ledgerKinds: LedgerItemKind[] = ['Opening', 'Payment', 'Payout', 'Cost', 'Credit', 'Water', 'Loss'];
  const s2 = golden<S2>('S2-ledger');
  const s4 = golden<S4>('S4-house-transfer');
  for (const ledger of [s2.houseA, s2.houseE, s4.ledgerNew, s4.ledgerOld]) {
    for (const item of ledger.items) expect(ledgerKinds).toContain(item.kind);
  }
  for (const name of ['S2-ledger', 'S3-ledger', 'S4-house-transfer']) {
    const text = readFileSync(new URL(`./golden/${name}.json`, import.meta.url), 'utf8');
    expect(text).not.toMatch(/ohňostroj|Silvestr|fond mimo|OffBook/i);
  }
});

// ───────────────────── S7 – rounding ─────────────────────

interface S7 { components: CostComponent[]; entries: CostEntry[]; allocation: CostEntryAllocation }

test('S7 rounding: 1 000,00 Kč EQUAL among 3 houses → 333,34 / 333,33 / 333,33', async ({ page }) => {
  const s7 = golden<S7>('S7-cost-allocation');
  const entry = s7.entries[0];
  await open(page, {
    '/cost-components': () => s7.components,
    '/documents': () => [],
    [`/cost-components/${entry.componentId}/entries`]: () => s7.entries,
    [`/cost-components/${entry.componentId}/entries/${entry.id}/allocation`]: () => s7.allocation,
  }, '/naklady');
  await expect(page.getByRole('heading', { name: 'Náklady', exact: true })).toBeVisible();

  const table = page.getByRole('table', { name: 'Nákladové záznamy' });
  await expect(table.getByRole('row', { name: /Zahradnictví Vzor/ })).toContainText(kc('1 000,00'));
  await table.getByRole('button', { name: 'Rozpad na domy' }).click();
  const totals = page.getByRole('table', { name: 'Součty za domy' });
  await expect(totals.getByRole('row')).toHaveCount(3);
  const expected: [string, string][] = [['RD A', '333,34'], ['RD B', '333,33'], ['RD C', '333,33']];
  for (const [house, amount] of expected) {
    await expect(totals.getByRole('row', { name: new RegExp(house) }).getByRole('cell').nth(1)).toHaveText(kc(amount));
  }
  await page.getByText('Výpočet po úsecích').click();
  await expect(page.getByLabel('Rozpad nákladu')).toContainText(/rovným dílem mezi 3 domy: RD A 333,34\s*Kč, RD B 333,33\s*Kč, RD C 333,33\s*Kč/);
});

// ───────────────────── S8 – reading interpolation ─────────────────────

test('S8 interpolation: meter state at 1. 11. 2023 suggested from readings is 260,855 m³, marked as an estimate', async ({ page }) => {
  const { estimate } = golden<{ estimate: ReadingEstimate }>('S8-reading-estimate');
  let asked: string | null = null;
  let saved: Record<string, unknown> | null = null;
  await open(page, {
    '/houses': () => [{ id: 'A', name: 'RD A', address: '', contactPerson: 'Vlastník A', email: '', isActive: true, dissolveOverpayment: false }],
    '/meters': () => [{ id: 'm1', meterNumber: 'V-001', name: '', type: 'Individual', houseId: 'A', houseName: 'RD A', radioAddress: null, installationDate: '2020-01-01T00:00:00Z' }],
    '/cost-components': () => [],
    '/ownership-periods': () => [{ id: 'A|2023-11-01', houseId: 'A', houseName: 'RD A', ownerName: 'Vlastník A', contact: null, validFrom: '2023-11-01', validTo: null }],
    '/opening-balances': (request) => {
      if (request.method() !== 'POST') return [];
      saved = request.postDataJSON();
      return { key: 'MeterReading|m1|A|2023-11-01' };
    },
    '/readings/estimate': (request) => { asked = `${query(request).get('meterId')}@${query(request).get('date')}`; return estimate; },
  }, '/admin/opening-balances');

  const meters = page.getByRole('region', { name: 'Stavy vodoměrů' });
  await meters.getByRole('button', { name: 'Navrhnout z odečtů' }).click();
  await expect(meters.getByLabel('Hodnota RD A')).toHaveValue('260,855');
  await expect(meters.getByRole('checkbox', { name: 'odhad' })).toBeChecked();
  await expect(meters.getByRole('status')).toContainText('mezi odečty 22. 5. 2023 (100 m³) a 19. 1. 2025 (700 m³), den 163 z 608');
  expect(asked).toBe('m1@2023-11-01');

  await meters.getByRole('button', { name: 'Uložit' }).click();
  await expect.poll(() => saved).not.toBeNull();
  expect(saved).toMatchObject({ type: 'MeterReading', meterId: 'm1', date: '2023-11-01', value: 260.855, isEstimate: true });
});
