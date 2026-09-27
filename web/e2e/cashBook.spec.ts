import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const entry = (over: Record<string, unknown>) => ({
  id: 'x', date: '2026-09-01', type: 'Deposit', amount: 0, effect: 0, balance: 0, category: 'výběr z účtu', description: '', counterparty: null,
  hasReceipt: true, documentId: null, bankTransactionRef: null, correctionOf: null, correctedBy: null, componentId: null, componentName: null,
  costEntryId: null, createdByName: 'Admin Test', ...over,
});

const book = {
  from: null, to: null, openingBalance: 0, deposits: 5000, expenses: 2000, closingBalance: 3000,
  entries: [
    entry({ id: 'd', amount: 5000, effect: 5000, balance: 5000, description: 'Výběr z účtu', bankTransactionRef: 'Fio 123' }),
    entry({ id: 'e', date: '2026-09-02', type: 'Expense', amount: 2000, effect: -2000, balance: 3000, category: 'údržba okolí', description: 'úprava okolí', counterparty: 'Pan Novák', hasReceipt: false }),
  ],
};

test('cash book (S5): expense without receipt is flagged, an expense over the balance shows the reason, storno needs a reason', async ({ page }) => {
  let storno: Record<string, unknown> | null = null;
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/cost-components': () => [],
    '/documents': () => [],
    '/cash-book': () => book,
    '/cash-book/e/storno': (request) => { storno = request.postDataJSON(); return entry({ id: 's', type: 'Correction' }); },
  });
  await page.route('**/api/cash-book', async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({
        error: 'Výdaj by způsobil záporný zůstatek pokladny (-1 000,00 Kč k 3. 9. 2026).',
        errors: [{ field: '', message: 'Výdaj by způsobil záporný zůstatek pokladny (-1 000,00 Kč k 3. 9. 2026).' }],
      }) });
      return;
    }
    await route.fallback();
  });

  await signInAsAdmin(page);
  await navigate(page, '/pokladna');
  await expect(page.getByRole('heading', { name: 'Pokladna' })).toBeVisible();

  const table = page.getByRole('table', { name: 'Záznamy pokladny' });
  await expect(table.getByRole('row', { name: /úprava okolí/ }).getByLabel('bez dokladu')).toBeVisible();
  await expect(page.getByText('3 000,00 Kč', { exact: true }).first()).toBeVisible();

  const form = page.getByRole('form', { name: 'Nový záznam pokladny' });
  await form.getByLabel('Částka (Kč)').fill('4000');
  await form.getByLabel('Co', { exact: true }).fill('další úprava');
  await form.getByRole('button', { name: 'Zapsat' }).click();
  await expect(form.getByRole('alert')).toContainText('záporný zůstatek');

  const row = table.getByRole('row', { name: /úprava okolí/ });
  await row.getByRole('button', { name: 'Storno' }).click();
  const stornoButton = table.getByRole('button', { name: /Stornovat 2\s000,00\sKč/ });
  await expect(stornoButton).toBeDisabled();
  await table.getByLabel('Důvod storna').fill('zaplaceno z účtu');
  await stornoButton.click();
  await expect.poll(() => storno).toEqual({ reason: 'zaplaceno z účtu' });
});
