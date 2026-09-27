import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

test('expired session: a 401 from the API signs out and the login page says why (#17)', async ({ page }) => {
  const seen = await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
  });

  await signInAsAdmin(page);

  // The JWT expires: from now on every API call with it answers 401 (later routes win over mockApi).
  await page.route('**/api/cost-components**', (route) =>
    route.fulfill({ status: 401, contentType: 'application/json', body: JSON.stringify({ error: 'Neplatný token.' }) }));
  await navigate(page, '/naklady');

  await page.waitForURL('**/login');
  await expect(page.getByRole('status').filter({ hasText: 'Přihlášení vypršelo' })).toHaveText('Přihlášení vypršelo, přihlaste se znovu.');

  // No loop: once signed out, nothing is retried with the old token.
  const afterLogout = seen.length;
  await page.waitForTimeout(500);
  expect(seen.slice(afterLogout).filter((r) => r.headers().authorization)).toEqual([]);
});

test('a wrong magic link (401 on verify) is not an expired session', async ({ page }) => {
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
  });
  await page.route('**/api/auth/magic-link/verify', (route) =>
    route.fulfill({ status: 401, contentType: 'application/json', body: JSON.stringify({ error: 'Odkaz je neplatný nebo vypršel.' }) }));

  await page.goto('/auth/verify?token=bad&email=admin%40example.cz');
  await expect(page.getByText('Odkaz je neplatný nebo vypršel.')).toBeVisible();
  await expect(page.getByText('Přihlášení vypršelo, přihlaste se znovu.')).toHaveCount(0);
});
