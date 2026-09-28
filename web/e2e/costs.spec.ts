import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const component = {
  id: 'c-vodarna', name: 'Elektřina – vodárna', code: 'ELEKTRINA_VODARNA', startDate: '2023-11-01',
  allocationBasis: 'CostEntries', waterRole: 'None', active: true, note: null, currentMethod: 'Equal', currentParticipants: 4,
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
  await form.getByLabel('Od', { exact: true }).fill('2023-11-01');
  await form.getByLabel('Do', { exact: true }).fill('2024-10-31');
  await form.getByLabel('Úhrada').selectOption('SupplierCredit');
  await form.getByRole('button', { name: 'Vytvořit zálohy' }).click();

  await expect(form.getByRole('status')).toHaveText('Vytvořeno 12 záloh.');
  expect(recurring).toMatchObject({ amount: 500, periodicity: 'Monthly', from: '2023-11-01', to: '2024-10-31', paidFrom: 'SupplierCredit' });
});


test('costs: edit an entry, delete it with a confirmation and reason, add a correction with a reason (#10, #19)', async ({ page }) => {
  const writes: { method: string; path: string; query: string; body: unknown }[] = [];
  const correction = {
    ...advance, id: 'e2', type: 'OneOff', periodFrom: '2023-10-01', periodTo: '2023-10-31', amount: 900, supplier: null,
    note: null, postingDate: '2027-01-01', correctionOf: null,
  };
  const record = (request: import('@playwright/test').Request) => {
    const url = new URL(request.url());
    if (request.method() !== 'GET') writes.push({ method: request.method(), path: url.pathname, query: url.search, body: request.postDataJSON() });
  };
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/cost-components': () => [component],
    '/documents': () => [],
    '/cost-components/c-vodarna/entries': (request) => {
      record(request);
      return request.method() === 'POST' ? correction : [{ ...advance, postingDate: null, correctionOf: null }, correction];
    },
    '/cost-components/c-vodarna/entries/e1': (request) => {
      record(request);
      return request.method() === 'PUT' ? advance : {};
    },
  });

  await signInAsAdmin(page);
  await navigate(page, '/naklady');
  const table = page.getByRole('table', { name: 'Nákladové záznamy' });
  // A correction shows where it was booked.
  await expect(table.getByRole('row', { name: /1\. 10\. 2023/ })).toContainText('oprava, zaúčtováno 1. 1. 2027');

  // Edit
  const row = table.getByRole('row', { name: /1\. 11\. 2023/ });
  await row.getByRole('button', { name: 'Upravit' }).click();
  const edit = page.getByRole('form', { name: 'Upravit náklad' });
  await expect(edit.getByLabel('Částka (Kč)')).toHaveValue('500');
  await edit.getByLabel('Částka (Kč)').fill('550,50');
  await edit.getByRole('button', { name: 'Uložit změnu' }).click();
  await expect(edit).toHaveCount(0);
  expect(writes).toEqual([{
    method: 'PUT', path: '/api/cost-components/c-vodarna/entries/e1', query: '',
    body: expect.objectContaining({ type: 'Advance', periodFrom: '2023-11-01', periodTo: '2023-11-30', amount: 550.5, paidFrom: 'SupplierCredit', supplier: 'PRE' }),
  }]);

  // Delete: asks first; cancelling sends nothing, confirming sends the reason.
  await row.getByRole('button', { name: 'Smazat' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Zrušit' }).click();
  expect(writes).toHaveLength(1);
  await row.getByRole('button', { name: 'Smazat' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toContainText('Opravdu smazat záznam „Záloha“ 1. 11. 2023 – 30. 11. 2023');
  await dialog.getByLabel('Důvod (nepovinné, zapíše se do auditu)').fill('zadáno dvakrát');
  await dialog.getByRole('button', { name: 'Smazat' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(writes[1]).toMatchObject({ method: 'DELETE', path: '/api/cost-components/c-vodarna/entries/e1', query: `?reason=${encodeURIComponent('zadáno dvakrát')}` });

  // Add into a closed period: invalid amount first (#8), then with the reason → the API books a correction.
  const form = page.getByRole('form', { name: 'Přidat náklad' });
  await form.getByLabel('Druh').selectOption({ label: 'Jednorázový / faktura' });
  await form.getByLabel('Období od').fill('2023-10-01');
  await form.getByLabel('do', { exact: true }).fill('2023-10-31');
  await form.getByLabel('Částka (Kč)').fill('9OO');
  await form.getByRole('button', { name: 'Přidat' }).click();
  await expect(form.getByRole('alert')).toContainText('Částka (Kč): zadejte číslo');
  expect(writes).toHaveLength(2);

  await form.getByLabel('Částka (Kč)').fill('900');
  await form.getByLabel('Důvod (u opravy za mezizávěrkou)').fill('faktura doručena po závěrce');
  await form.getByRole('button', { name: 'Přidat' }).click();
  await expect(form.getByRole('status')).toContainText('uložen jako oprava');
  await expect(form.getByRole('status')).toContainText('1. 1. 2027');
  expect(writes[2]).toMatchObject({ method: 'POST', body: { amount: 900, reason: 'faktura doručena po závěrce', periodFrom: '2023-10-01' } });
});
