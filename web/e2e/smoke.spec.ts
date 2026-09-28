import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

test('login page shows the test-environment strip reported by the API', async ({ page }) => {
  await mockApi(page, { '/environment': () => ({ environment: 'test' }) });

  await page.goto('/login');

  await expect(page.getByRole('status')).toContainText('TESTOVACÍ PROSTŘEDÍ');
});

test('production API hides the environment strip', async ({ page }) => {
  await mockApi(page, { '/environment': () => ({ environment: 'prod' }) });

  await page.goto('/login');
  await page.waitForResponse('**/api/environment');

  // The build under test has no VITE_ENVIRONMENT, so only the API decides.
  await expect(page.getByRole('status')).toHaveCount(0);
});

const houses = [
  { houseId: 'h1', houseName: 'RD1', waterAmount: 1000, electricityAmount: 200, commonAmount: 300, totalAmount: 1500 },
  { houseId: 'h4', houseName: 'RD4', waterAmount: 1000, electricityAmount: 200, commonAmount: 300, totalAmount: 1500 },
];

const row = (overrides: Record<string, unknown>) => ({
  date: '2026-08-12T00:00:00Z',
  amount: 1500,
  currency: 'CZK',
  counterName: 'Plátce',
  message: null,
  note: null,
  variableSymbol: null,
  bankOperationType: 'Bezhotovostní příjem',
  status: 'New',
  houseId: null,
  matchSource: 'None',
  paymentType: 'Doplatek',
  year: 2026,
  month: 8,
  waterAmount: 1500,
  electricityAmount: 0,
  commonAmount: 0,
  warnings: [],
  ...overrides,
});

test('bank import: unassigned payment blocks confirm until a house is picked', async ({ page }) => {
  let confirmBody: { account: string; rows: { transactionId: string; houseId: string | null; paymentType: string }[] } | null = null;
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/bank-import/preview': () => ({
      statement: {
        account: '2601634649/2010', number: '8/2026', dateFrom: '2026-08-01T00:00:00Z', dateTo: '2026-08-31T00:00:00Z',
        openingBalance: 1000, closingBalance: 4000, totalIncome: 3000, totalExpense: 0, sumCheckOk: true,
      },
      rows: [
        row({ transactionId: 't1', counterAccount: '111111111/0300', counterName: 'Jana Testová', message: 'RD1',
              houseId: 'h1', matchSource: 'Account', paymentType: 'Advance', waterAmount: 1000, electricityAmount: 200, commonAmount: 300 }),
        row({ transactionId: 't2', counterAccount: '3333333333/5500', counterName: 'Petr Vzorový', message: 'RD4' }),
      ],
      warnings: [],
      houses,
      existingAdvanceMonths: {},
    }),
    '/bank-import/confirm': (request) => {
      confirmBody = request.postDataJSON();
      return { imported: 2, ignored: 0, newAccounts: 1, skipped: [] };
    },
  });

  await signInAsAdmin(page);
  await navigate(page, '/advances/import');
  await expect(page.getByRole('heading', { name: 'Import z banky' })).toBeVisible();

  await page.setInputFiles('input[type=file]', { name: 'vypis.csv', mimeType: 'text/csv', buffer: Buffer.from('"ID operace"') });
  await page.getByRole('button', { name: 'Načíst náhled' }).click();

  const confirm = page.getByRole('button', { name: 'Potvrdit import' });
  await expect(page.getByText('Jana Testová')).toBeVisible();
  await expect(confirm).toBeDisabled();

  // Picking RD4 re-suggests the prescribed advance and its split.
  const unassigned = page.locator('tr', { hasText: 'Petr Vzorový' });
  await unassigned.locator('select').first().selectOption({ label: 'RD4' });
  await expect(unassigned.locator('select').nth(1)).toHaveValue('Advance');
  await expect(unassigned.getByText('ručně — účet se uloží k domácnosti')).toBeVisible();
  await expect(confirm).toBeEnabled();

  await confirm.click();
  await expect(page.getByText('Naimportováno 2')).toBeVisible();

  expect(confirmBody).not.toBeNull();
  expect(confirmBody!.account).toBe('2601634649/2010');
  expect(confirmBody!.rows.map((r) => [r.transactionId, r.houseId, r.paymentType])).toEqual([
    ['t1', 'h1', 'Advance'],
    ['t2', 'h4', 'Advance'],
  ]);
});
