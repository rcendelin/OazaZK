import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const houses = ['A', 'B', 'C', 'D', 'E', 'F'].map((h) => ({ houseId: h, houseName: `RD ${h}`, expected: 1230, paid: h === 'F' ? 0 : 1230, isPaid: h !== 'F' }));

test('off-book fund on: warning is always shown and the call shows one debtor (S6)', async ({ page }) => {
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/features': () => ({ offBookFund: true }),
    '/houses': () => [],
    '/off-book-funds': () => [{ id: 'f1', name: 'Fond na ohňostroje', purpose: null, managerName: 'Jindra', accountDescription: 'soukromý účet správce', active: true, balance: 6150, outstanding: 0 }],
    '/off-book-funds/f1': () => ({
      fund: { id: 'f1', name: 'Fond na ohňostroje', purpose: null, managerName: 'Jindra', accountDescription: 'soukromý účet správce', active: true, balance: 6150, outstanding: 0 },
      calls: [{ id: 'c1', date: '2026-12-01', dueDate: '2026-12-15', amountPerHouse: 1230, text: 'Silvestr 2026', collected: 6150, debtors: 1, houses }],
      records: [],
    }),
  });

  await signInAsAdmin(page);
  await page.getByRole('link', { name: 'Hospodaření' }).click();
  await expect(page.getByRole('link', { name: 'Oddělený fond' })).toBeVisible();
  await navigate(page, '/fond');

  await expect(page.getByRole('note')).toHaveText('Fond mimo účetnictví spolku – peníze nejsou na účtu spolku.');
  await expect(page.getByText(/6\s150,00\sKč/).first()).toBeVisible();
  const call = page.getByLabel('Výzva Silvestr 2026');
  await expect(call).toContainText('nezaplatil 1 dům');
  await expect(call).toContainText('RD F: chybí');
});

test('off-book fund off: no menu item', async ({ page }) => {
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/features': () => ({ offBookFund: false }),
  });

  await signInAsAdmin(page);
  await page.getByRole('link', { name: 'Hospodaření' }).click();
  await expect(page.getByRole('link', { name: 'Pokladna' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Oddělený fond' })).toHaveCount(0);
});
