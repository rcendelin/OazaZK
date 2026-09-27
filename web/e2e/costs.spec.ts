import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const component = {
  id: 'c-vodarna', name: 'Elektřina – vodárna', code: 'ELEKTRINA_VODARNA', startDate: '2023-11-01',
  allocationBasis: 'CostEntries', active: true, note: null, currentMethod: 'Equal', currentParticipants: 4,
};
const advance = {
  id: 'e1', componentId: 'c-vodarna', componentName: 'Elektřina – vodárna', type: 'Advance', periodFrom: '2023-11-01', periodTo: '2023-11-30',
  days: 30, amount: 500, quantityM3: null, supplier: 'PRE', documentId: null, paidFrom: 'SupplierCredit', note: null, locked: false,
};
const shares = ['RD1', 'RD2', 'RD3', 'RD4'].map((houseName, i) => ({ houseId: `h${i + 1}`, houseName, weight: 1, amount: 125 }));

test('costs: advance paid from supplier credit shows its split, recurring advance posts the series', async ({ page }) => {
  let recurring: Record<string, unknown> | null = null;
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/cost-components': () => [component],
    '/documents': () => [],
    '/cost-components/c-vodarna/entries': () => [advance],
    '/cost-components/c-vodarna/entries/e1/allocation': () => ({
      entry: advance,
      segments: [{ from: '2023-11-01', to: '2023-11-30', days: 30, method: 'Equal', amount: 500, shares }],
      houseTotals: shares,
    }),
    '/cost-components/c-vodarna/entries/recurring': (request) => {
      recurring = request.postDataJSON();
      return Array.from({ length: 12 }, (_, i) => ({ ...advance, id: `r${i}` }));
    },
  });

  await signInAsAdmin(page);
  await navigate(page, '/naklady');
  await expect(page.getByRole('heading', { name: 'Náklady', exact: true })).toBeVisible();

  const table = page.getByRole('table', { name: 'Nákladové záznamy' });
  await expect(table.getByText('Z přeplatku u dodavatele')).toBeVisible();
  await table.getByRole('button', { name: 'Rozpad na domy' }).click();
  await expect(page.getByRole('table', { name: 'Součty za domy' }).getByRole('row')).toHaveCount(4);
  await expect(page.getByRole('table', { name: 'Součty za domy' })).toContainText('125,00 Kč');

  const form = page.getByRole('form', { name: 'Opakovaná záloha' });
  await form.getByLabel('Částka (Kč)').fill('500');
  await form.getByLabel('Od').fill('2023-11-01');
  await form.getByLabel('Do').fill('2024-10-31');
  await form.getByLabel('Úhrada').selectOption('SupplierCredit');
  await form.getByRole('button', { name: 'Vytvořit zálohy' }).click();

  await expect(form.getByRole('status')).toHaveText('Vytvořeno 12 záloh.');
  expect(recurring).toMatchObject({ amount: 500, periodicity: 'Monthly', from: '2023-11-01', to: '2024-10-31', paidFrom: 'SupplierCredit' });
});
