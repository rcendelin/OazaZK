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
