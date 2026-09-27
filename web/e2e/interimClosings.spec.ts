import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const closing = {
  id: '2026-12-31|All|-', date: '2026-12-31', scope: 'All', houseId: null, houseName: null, reason: 'roční závěrka 2026',
  createdByName: 'Admin Test', createdAt: '2027-01-05T08:00:00Z', totalSaldo: -80, canDelete: true, houses: null,
};

test('interim closings: creating the annual closing and its detail with snapshot vs today', async ({ page }) => {
  let created: Record<string, unknown> | null = null;
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/houses': () => [],
    '/interim-closings': (request) => {
      if (request.method() === 'POST') {
        created = request.postDataJSON();
        return closing;
      }
      return created ? [closing] : [];
    },
    '/interim-closings/2026-12-31%7CAll%7C-': () => ({
      ...closing,
      houses: [
        { houseId: 'A', houseName: 'RD A', opening: 0, payments: 0, costs: 80, saldo: -80, currentSaldo: -80, difference: 0 },
        { houseId: 'E', houseName: 'RD E', opening: 0, payments: 0, costs: 0, saldo: 0, currentSaldo: 0, difference: 0 },
      ],
    }),
  });

  await signInAsAdmin(page);
  await navigate(page, '/mezizaverky');
  await expect(page.getByRole('heading', { name: 'Mezizávěrky' })).toBeVisible();
  await expect(page.getByText('Zatím žádná mezizávěrka')).toBeVisible();

  const form = page.getByRole('form', { name: 'Nová mezizávěrka' });
  await form.getByLabel('Uzavřít do (včetně)').fill('2026-12-31');
  await form.getByLabel('Důvod').fill('roční závěrka 2026');
  await form.getByRole('button', { name: 'Uzavřít' }).click();

  await expect.poll(() => created).not.toBeNull();
  expect(created).toEqual({ date: '2026-12-31', scope: 'All', reason: 'roční závěrka 2026' });

  const detail = page.getByRole('region', { name: 'Mezizávěrka k 31. 12. 2026' });
  await expect(detail.getByText('Saldo k datu mezizávěrky odpovídá snímku.')).toBeVisible();
  await expect(detail.getByRole('table', { name: 'Snímek salda' }).getByRole('row')).toHaveCount(3);
  await expect(detail.getByRole('button', { name: 'Export pro účetní (XLSX)' })).toBeVisible();
  await expect(detail.getByRole('button', { name: 'Zrušit mezizávěrku' })).toBeDisabled();
});
