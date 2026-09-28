import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const component = {
  id: 'c-vodarna', name: 'Elektřina – vodárna', code: 'ELEKTRINA_VODARNA', startDate: '2023-11-01',
  allocationBasis: 'CostEntries', waterRole: 'None', active: true, note: null, currentMethod: 'Equal', currentParticipants: 4,
};
const participation = (houseId: string, houseName: string) => ({
  id: `p-${houseId}`, houseId, houseName, validFrom: '2023-11-01', validTo: null, weight: null,
});
const participants = [participation('h1', 'RD1'), participation('h2', 'RD2'), participation('h3', 'RD3'), participation('h4', 'RD4')];
const houses = ['RD1', 'RD2', 'RD3', 'RD4', 'RD5'].map((name, i) => ({
  id: `h${i + 1}`, name, address: '', contactPerson: '', email: '', isActive: true, dissolveOverpayment: false,
}));

test('cost components: waterworks with 4 houses shows its segment and adds a house from a date', async ({ page }) => {
  let added: { houseId: string; validFrom: string; validTo?: string } | null = null;
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/houses': () => houses,
    '/cost-components': () => [component],
    '/cost-components/c-vodarna': () => ({
      component,
      rules: [{ id: 'r1', validFrom: '2023-11-01', validTo: null, method: 'Equal', ratioSource: null, reason: 'Založení složky' }],
      participations: participants,
      lastClosedDay: null,
    }),
    '/cost-components/c-vodarna/segments': () => [
      { from: '2025-09-27', to: '2026-09-27', days: 366, method: 'Equal', participants },
    ],
    '/cost-components/c-vodarna/participations': (request) => {
      added = request.postDataJSON();
      return participation('h5', 'RD5');
    },
  });

  await signInAsAdmin(page);
  await navigate(page, '/admin/cost-components');
  await expect(page.getByRole('heading', { name: 'Nákladové složky' })).toBeVisible();

  const detail = page.getByRole('region', { name: 'Detail složky Elektřina – vodárna' });
  await expect(detail).toBeVisible();
  await expect(detail.getByText('Rovným dílem · 1. 11. 2023 – dosud')).toBeVisible();

  const segments = page.getByRole('table', { name: 'Úseky' });
  await expect(segments.getByText('RD1, RD2, RD3, RD4')).toBeVisible();

  const form = page.getByRole('form', { name: 'Přidat účast' });
  await form.getByRole('combobox').selectOption({ label: 'RD5' });
  await form.locator('input[type=date]').first().fill('2026-10-01');
  await form.getByRole('button', { name: 'Přidat účast' }).click();

  await expect.poll(() => added).not.toBeNull();
  expect(added).toEqual({ houseId: 'h5', validFrom: '2026-10-01' });
});


test('cost components: edit name/active/note, delete a mistaken rule and a participation only after confirmation (#19)', async ({ page }) => {
  const writes: { method: string; path: string; query: string; body: unknown }[] = [];
  const record = (request: import('@playwright/test').Request) => {
    const url = new URL(request.url());
    if (request.method() !== 'GET') writes.push({ method: request.method(), path: url.pathname, query: url.search, body: request.postDataJSON() });
  };
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/houses': () => houses,
    '/cost-components': () => [component],
    '/cost-components/c-vodarna': (request) => {
      record(request);
      return {
        component,
        rules: [
          { id: 'r1', validFrom: '2023-11-01', validTo: '2026-09-30', method: 'Equal', ratioSource: null, reason: 'Založení složky' },
          { id: 'r2', validFrom: '2026-10-01', validTo: null, method: 'Percent', ratioSource: null, reason: 'omyl' },
        ],
        participations: participants,
        lastClosedDay: null,
      };
    },
    '/cost-components/c-vodarna/rules/r2': (request) => { record(request); return {}; },
    '/cost-components/c-vodarna/participations/p-h2': (request) => { record(request); return {}; },
  });

  await signInAsAdmin(page);
  await navigate(page, '/admin/cost-components');
  const detail = page.getByRole('region', { name: 'Detail složky Elektřina – vodárna' });

  // Edit the component
  await detail.getByRole('button', { name: 'Upravit složku' }).click();
  const edit = detail.getByRole('form', { name: 'Upravit složku' });
  await edit.getByLabel('Název').fill('Elektřina vodárna');
  await edit.getByLabel('Poznámka').fill('PRE');
  await edit.getByLabel('aktivní').uncheck();
  await edit.getByRole('button', { name: 'Uložit složku' }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(writes[0]).toEqual({ method: 'PUT', path: '/api/cost-components/c-vodarna', query: '', body: { name: 'Elektřina vodárna', active: false, note: 'PRE' } });

  // Delete the mistaken rule with a reason
  await detail.getByRole('button', { name: 'Smazat pravidlo Pevná procenta od 1. 10. 2026' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toContainText('prodlouží předchozí pravidlo');
  await dialog.getByLabel('Důvod (nepovinné, zapíše se do auditu)').fill('zadáno omylem');
  await dialog.getByRole('button', { name: 'Smazat pravidlo' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(writes[1]).toMatchObject({ method: 'DELETE', path: '/api/cost-components/c-vodarna/rules/r2', query: `?reason=${encodeURIComponent('zadáno omylem')}` });

  // Deleting a participation asks first; cancelling sends nothing
  const rd2 = detail.getByRole('listitem').filter({ hasText: 'RD2' });
  await rd2.getByRole('button', { name: 'Smazat' }).click();
  await expect(page.getByRole('dialog')).toContainText('Smazat účast domu RD2');
  await page.getByRole('dialog').getByRole('button', { name: 'Zrušit' }).click();
  expect(writes).toHaveLength(2);
  await rd2.getByRole('button', { name: 'Smazat' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Smazat účast' }).click();
  await expect.poll(() => writes.length).toBe(3);
  expect(writes[2]).toMatchObject({ method: 'DELETE', path: '/api/cost-components/c-vodarna/participations/p-h2', query: '' });
});
