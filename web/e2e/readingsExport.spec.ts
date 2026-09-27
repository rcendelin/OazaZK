import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

test('readings: export with the estimate flag asks for the chosen range and format', async ({ page }) => {
  const seen = await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/readings/export': () => 'Datum;Vodoměr',
  });

  await signInAsAdmin(page);
  await navigate(page, '/readings/list');
  const section = page.getByRole('region', { name: 'Export odečtů' });
  await expect(section.getByText('sloupec „Odhad“')).toBeVisible();
  await section.getByLabel('Od', { exact: true }).fill('2026-01-01');
  await section.getByLabel('Do', { exact: true }).fill('2026-06-30');

  const download = page.waitForEvent('download');
  await section.getByRole('button', { name: 'CSV' }).click();
  await download;

  const request = seen.find((r) => r.url().includes('/readings/export'));
  expect(request).toBeDefined();
  const query = new URL(request!.url()).searchParams;
  expect(Object.fromEntries(query)).toEqual({ format: 'csv', from: '2026-01-01', to: '2026-06-30' });
});
