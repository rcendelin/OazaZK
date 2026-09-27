import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const overview = {
  from: '2023-11-01', to: '2023-12-31',
  components: [
    { componentId: 'vodarna', componentName: 'Elektřina – vodárna', allocatedTotal: -19000, housesTotal: -19000, matches: true, warnings: [] },
    { componentId: 'jimka', componentName: 'Odvoz jímky', allocatedTotal: 3000, housesTotal: 2999.99, matches: false, warnings: ['Náklad 5. 12. 2023: ke dni 5. 12. 2023 se složky neúčastní žádný dům.'] },
  ],
  houses: [
    { houseId: 'A', houseName: 'RD A', opening: 0, payments: 0, costs: { vodarna: -4750 }, saldo: 4750 },
    { houseId: 'E', houseName: 'RD E', opening: 0, payments: 0, costs: {}, saldo: 0 },
  ],
};

const ledgerA = {
  houseId: 'A', houseName: 'RD A', from: '2023-11-01', to: '2023-12-31',
  ownershipPeriod: { id: 'A|2023-11-01', ownerName: 'Vlastník A', validFrom: '2023-11-01', validTo: null },
  ownershipPeriods: [{ id: 'A|2023-11-01', ownerName: 'Vlastník A', validFrom: '2023-11-01', validTo: null }],
  opening: 0, payments: 0, costs: -4750, saldo: 4750,
  items: [
    { date: '2023-11-01', kind: 'Credit', description: 'Kredit u dodavatele (počáteční stav)', componentId: 'vodarna', componentName: 'Elektřina – vodárna', amount: 5000, balance: 5000,
      detail: { total: -20000, segmentFrom: '2023-11-01', segmentTo: '2023-11-01', segmentDays: 1, segmentAmount: -20000, method: 'Equal', weight: 1, totalWeight: 4,
        explanation: 'Kredit −20 000,00 Kč k 1. 11. 2023 (PRE), rovným dílem mezi 4 domů: podíl 1 z 4 = −5 000,00 Kč' } },
    { date: '2023-11-01', kind: 'Cost', description: 'Záloha 1. 11. 2023 – 30. 11. 2023 (PRE)', componentId: 'vodarna', componentName: 'Elektřina – vodárna', amount: -125, balance: 4875, detail: null },
    { date: '2023-12-01', kind: 'Cost', description: 'Záloha 1. 12. 2023 – 31. 12. 2023 (PRE)', componentId: 'vodarna', componentName: 'Elektřina – vodárna', amount: -125, balance: 4750, detail: null },
  ],
};

test('ledger (admin): control row flags a mismatch, a house opens with its running saldo and calculation', async ({ page }) => {
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/ledger/overview': () => overview,
    '/ledger/houses/A': () => ledgerA,
  });

  await signInAsAdmin(page);
  await navigate(page, '/saldo-domu');
  await expect(page.getByRole('heading', { name: 'Saldo domu', exact: true })).toBeVisible();

  const table = page.getByRole('table', { name: 'Saldo domů' });
  await expect(table.getByRole('row', { name: /RD A/ })).toContainText('4 750,00 Kč přeplatek');
  await expect(table.getByText(/✗ 2 999,99 Kč ≠ 3 000,00 Kč/)).toBeVisible();
  await expect(page.getByRole('alert')).toContainText('neúčastní žádný dům');

  await table.getByRole('row', { name: /RD A/ }).click();
  const detail = page.getByRole('region', { name: 'Saldo RD A' });
  await expect(detail.getByRole('table', { name: 'Položky salda' }).getByRole('row')).toHaveCount(4);
  await detail.getByText('Jak vznikl').click();
  await expect(detail.getByText(/rovným dílem mezi 4 domů/)).toBeVisible();
});

test('ledger (member): own house detail without choosing, other houses only as a summary', async ({ page }) => {
  const member = { ...adminUser, id: 'u-member', role: 'Member', houseId: 'A' };
  const requested: string[] = [];
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => member,
    '/ledger/overview': () => overview,
    '/ledger/houses/A': (request) => { requested.push(new URL(request.url()).pathname); return ledgerA; },
  });

  await signInAsAdmin(page);
  await navigate(page, '/saldo-domu');

  await expect(page.getByRole('region', { name: 'Saldo RD A' })).toBeVisible();
  await page.getByRole('table', { name: 'Saldo domů' }).getByRole('row', { name: /RD E/ }).click();
  await expect(page.getByRole('region', { name: 'Saldo RD E' })).toHaveCount(0);
  expect(requested.every((p) => p.endsWith('/ledger/houses/A'))).toBe(true);
});
