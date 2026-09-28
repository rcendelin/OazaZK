import type { Page } from '@playwright/test';
import { RUN, api, expect, go, kc, loginAs, test, users } from './live';
import { TODAY, componentByCode, download, isPdf, isXlsx, json, shift } from './data';

/**
 * T09 Pokladna (S5): vklad, výdaj bez dokladu (musí mít „komu“), výdaj jako náklad složky, přečerpání odmítnuto,
 * storno (i navázaného nákladu), exporty, účetní zapisuje, člen jen čte.
 */
interface Book { openingBalance: number; deposits: number; expenses: number; closingBalance: number; entries: { id: string; date: string; type: string; amount: number; description: string; correctedBy: string | null; componentName: string | null; hasReceipt: boolean }[] }
interface Entry { id: string; amount: number; paidFrom: string; note: string | null; periodFrom: string }

const TAG = `${RUN}-${Date.now().toString(36).slice(-4)}`;
const book = (ctx: Parameters<typeof json>[0]) => json<Book>(ctx, 'cash-book');

async function openCash(page: Page) {
  await go(page, '/pokladna');
  await expect(page.getByRole('heading', { name: 'Pokladna' })).toBeVisible();
  await expect(page.locator('.animate-spin')).toHaveCount(0, { timeout: 30_000 });
}

async function write(page: Page, e: { type: 'Deposit' | 'Expense'; amount: string; description: string; counterparty?: string; receipt?: boolean; component?: string; date?: string; bankRef?: string }) {
  const form = page.getByRole('form', { name: 'Nový záznam pokladny' });
  await form.getByLabel('Druh').selectOption(e.type);
  await form.getByLabel('Datum').fill(e.date ?? TODAY);
  await form.getByLabel('Částka (Kč)').fill(e.amount);
  await form.getByLabel('Co', { exact: true }).fill(e.description);
  await form.getByLabel(e.type === 'Deposit' ? 'Od koho' : 'Komu', { exact: true }).fill(e.counterparty ?? '');
  if (e.type === 'Expense') {
    const box = form.getByRole('checkbox');
    if ((await box.isChecked()) !== (e.receipt ?? true)) await box.click();
    await form.getByLabel('Společný náklad složky').selectOption(e.component ? { label: e.component } : '');
  } else if (e.bankRef) {
    await form.getByLabel('Pohyb v bance').fill(e.bankRef);
  }
  await form.getByRole('button', { name: 'Zapsat' }).click();
  return form;
}

test.describe.serial('pokladna (T09, S5)', () => {
  test('vklad, výdaj bez dokladu, přečerpání a budoucí datum odmítnuty, storno vrátí zůstatek', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const start = (await book(admin)).closingBalance;
    await loginAs(page, users.admin);
    await openCash(page);

    let form = await write(page, { type: 'Deposit', amount: '5 000', description: `výběr z účtu ${TAG}`, bankRef: 'výpis 9/2026' });
    await expect(form.getByRole('alert')).toHaveCount(0);
    await expect.poll(async () => (await book(admin)).closingBalance).toBe(start + 5000);

    // expense without a receipt and without „Komu“ → refused
    form = await write(page, { type: 'Expense', amount: '2000', description: `úprava okolí ${TAG}`, receipt: false });
    await expect(form.getByText('Výdaj bez dokladu: vyplňte, co se zaplatilo a komu.')).toBeVisible();
    await expect(form.getByRole('alert')).toBeVisible();
    form = await write(page, { type: 'Expense', amount: '2000', description: `úprava okolí ${TAG}`, receipt: false, counterparty: `zahradník ${TAG}` });
    await expect(form.getByRole('alert')).toHaveCount(0);
    await expect.poll(async () => (await book(admin)).closingBalance).toBe(start + 3000);
    const table = page.getByRole('table', { name: 'Záznamy pokladny' });
    const expenseRow = table.getByRole('row').filter({ hasText: `úprava okolí ${TAG}` }).filter({ hasText: 'Výdaj ·' });
    await expect(expenseRow).toContainText('bez dokladu');
    await expect(page.getByText('Zůstatek v pokladně').locator('..')).toContainText(kc(start + 3000));

    // overdraft: more than the balance → refused
    form = await write(page, { type: 'Expense', amount: String(start + 3000 + 1000), description: `přečerpání ${TAG}`, counterparty: 'x' });
    await expect(form.getByRole('alert')).toContainText(/zůstatek|mínus|záporn|nedostatek/i);
    // future date → refused
    form = await write(page, { type: 'Deposit', amount: '1', description: `budoucnost ${TAG}`, date: shift(TODAY, 1) });
    await expect(form.getByRole('alert')).toBeVisible();
    expect((await book(admin)).closingBalance).toBe(start + 3000);

    // storno of the 2 000 expense
    await expenseRow.getByRole('button', { name: 'Storno' }).click();
    const stornoBox = page.getByRole('row').filter({ has: page.getByText('Důvod storna') });
    await expect(stornoBox.getByRole('button', { name: /^Stornovat/ })).toBeDisabled();
    await stornoBox.getByLabel('Důvod storna').fill(`omyl ${TAG}`);
    await stornoBox.getByRole('button', { name: /^Stornovat 2\s?000,00\s?Kč$/ }).click();
    await expect.poll(async () => (await book(admin)).closingBalance).toBe(start + 5000);
    await expect(expenseRow).toHaveClass(/line-through/);
    await expect(expenseRow.getByRole('button', { name: 'Storno' })).toHaveCount(0);
    const b = await book(admin);
    const original = b.entries.find((e) => e.description === `úprava okolí ${TAG}`)!;
    expect(original.correctedBy).not.toBeNull();
    // second storno of the same entry via API → refused
    expect((await admin.post(`cash-book/${original.id}/storno`, { data: { reason: 'znovu' } })).status()).toBeGreaterThanOrEqual(400);
  });

  test('výdaj jako společný náklad složky vytvoří jednorázový náklad (hotově); storno ho opraví záporným nákladem', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const osv = (await componentByCode(admin, 'OSVETLENI'))!;
    const entries = () => json<Entry[]>(admin, `cost-components/${osv.id}/entries?from=${TODAY}&to=${TODAY}`);
    const beforeIds = new Set((await entries()).map((e) => e.id));
    await loginAs(page, users.admin);
    await openCash(page);
    const form = await write(page, { type: 'Expense', amount: '300', description: `žárovky ${TAG}`, component: osv.name, counterparty: 'železářství' });
    await expect(form.getByRole('alert')).toHaveCount(0);
    const row = page.getByRole('table', { name: 'Záznamy pokladny' }).getByRole('row').filter({ hasText: `žárovky ${TAG}` }).filter({ hasText: 'Výdaj ·' });
    await expect(row).toContainText(`náklad ${osv.name}`);
    await expect.poll(async () => (await entries()).filter((e) => !beforeIds.has(e.id)).length).toBe(1);
    const created = (await entries()).find((e) => !beforeIds.has(e.id))!;
    expect(created).toMatchObject({ amount: 300, paidFrom: 'Cash' });

    await row.getByRole('button', { name: 'Storno' }).click();
    const stornoBox = page.getByRole('row').filter({ has: page.getByText('Důvod storna') });
    await stornoBox.getByLabel('Důvod storna').fill(`vráceno ${TAG}`);
    await stornoBox.getByRole('button', { name: /^Stornovat/ }).click();
    await expect.poll(async () => (await entries()).filter((e) => !beforeIds.has(e.id)).length).toBe(2);
    const added = (await entries()).filter((e) => !beforeIds.has(e.id));
    expect(added.reduce((s, e) => s + e.amount, 0)).toBeCloseTo(0, 2); // the storno correction cancels the cost
  });

  test('exporty XLSX a PDF', async ({ page }) => {
    await loginAs(page, users.admin);
    await openCash(page);
    const x = await download(page, () => page.getByRole('button', { name: 'Export XLSX' }).click(), /xlsx/);
    expect(isXlsx(x.body)).toBe(true);
    expect(x.name).toMatch(/\.xlsx$/);
    const p = await download(page, () => page.getByRole('button', { name: 'Export PDF' }).click(), /pdf/);
    expect(isPdf(p.body)).toBe(true);
    expect(p.name).toMatch(/\.pdf$/);
  });

  test('účetní zapisuje i stornuje; člen jen čte (bez formuláře, bez storna, vidí i výdaje bez dokladu)', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const start = (await book(admin)).closingBalance;
    await loginAs(page, users.accountant);
    await openCash(page);
    await write(page, { type: 'Deposit', amount: '1', description: `vklad účetní ${TAG}` });
    await expect.poll(async () => (await book(admin)).closingBalance).toBe(start + 1);
    const row = page.getByRole('table', { name: 'Záznamy pokladny' }).getByRole('row').filter({ hasText: `vklad účetní ${TAG}` }).filter({ hasText: 'Vklad ·' });
    await row.getByRole('button', { name: 'Storno' }).click();
    const stornoBox = page.getByRole('row').filter({ has: page.getByText('Důvod storna') });
    await stornoBox.getByLabel('Důvod storna').fill(`test ${TAG}`);
    await stornoBox.getByRole('button', { name: /^Stornovat/ }).click();
    await expect.poll(async () => (await book(admin)).closingBalance).toBe(start);

    const memberPage = await page.context().browser()!.newPage();
    await loginAs(memberPage, users.member);
    await openCash(memberPage);
    await expect(memberPage.getByRole('form', { name: 'Nový záznam pokladny' })).toHaveCount(0);
    await expect(memberPage.getByRole('button', { name: 'Storno' })).toHaveCount(0);
    await expect(memberPage.getByRole('button', { name: 'Export XLSX' })).toHaveCount(0);
    await expect(memberPage.getByRole('table', { name: 'Záznamy pokladny' }).getByText('bez dokladu').first()).toBeVisible();
    await memberPage.close();
    const member = await api(playwright, users.member);
    expect((await member.post('cash-book', { data: { date: TODAY, type: 'Deposit', amount: 1, category: 'x', description: 'x', hasReceipt: true } })).status()).toBe(403);
  });
});
