import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const doc = {
  id: 'd1', category: 'stanovy', name: 'Stanovy spolku', fileSizeBytes: 2048, contentType: 'application/pdf',
  uploadedAt: '2026-09-20T10:00:00Z', uploadedBy: 'u-admin', componentId: null,
};

test('documents: upload offers „Faktury a vyúčtování“, refuses >20 MB and shows the server reason (#18)', async ({ page }) => {
  let uploadUrl: string | null = null;
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/documents': () => [doc],
  });
  await signInAsAdmin(page);
  await navigate(page, '/documents');
  await expect(page.getByRole('heading', { name: 'Dokumenty' })).toBeVisible();

  await page.getByRole('button', { name: 'Nahrát dokument' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Název dokumentu').fill('Faktura PRE 09/2026');
  await dialog.getByLabel('Kategorie').selectOption({ label: 'Faktury a vyúčtování' });
  await expect(dialog.getByText('Faktury jen jako PDF, JPG nebo PNG.')).toBeVisible();

  // Too big: explained instead of silently ignored.
  await dialog.locator('input[type=file]').setInputFiles({
    name: 'velka.pdf', mimeType: 'application/pdf', buffer: Buffer.alloc(20 * 1024 * 1024 + 1),
  });
  await expect(dialog.getByRole('alert')).toHaveText('Soubor je větší než 20 MB.');

  // The server refuses the file: its reason is shown.
  await page.route((url) => url.pathname === '/api/documents' && url.searchParams.has('category'), (route) => {
    uploadUrl = route.request().url();
    return route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ error: 'Faktury lze nahrát jen jako PDF, JPG nebo PNG.' }) });
  });
  await dialog.locator('input[type=file]').setInputFiles({ name: 'faktura.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4') });
  await dialog.getByRole('button', { name: 'Nahrát', exact: true }).click();
  await expect(dialog.getByText('Nahrávání se nezdařilo. Faktury lze nahrát jen jako PDF, JPG nebo PNG.')).toBeVisible();
  expect(uploadUrl).toContain('category=faktury');
});

test('documents: version history shows the current version on top (#21c)', async ({ page }) => {
  let versions: unknown[] = [];
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/documents': () => [doc],
    '/documents/d1/versions': () => versions,
  });
  await signInAsAdmin(page);
  await navigate(page, '/documents');

  // Only the original upload: it is the current version.
  await page.getByRole('button', { name: 'Zobrazit' }).click();
  const history = page.getByRole('table', { name: 'Verze Stanovy spolku' });
  await expect(history.getByRole('row')).toHaveCount(2);
  await expect(history.getByRole('row').nth(1)).toContainText('aktuální');
  await expect(history.getByRole('row').nth(1)).toContainText('20. 9. 2026');

  // With uploaded versions the newest one is marked as current and listed first.
  versions = [
    { versionNumber: 1, fileSizeBytes: 1024, contentType: 'application/pdf', uploadedAt: '2026-09-21T10:00:00Z', uploadedBy: 'u-admin' },
    { versionNumber: 2, fileSizeBytes: 2048, contentType: 'application/pdf', uploadedAt: '2026-09-22T10:00:00Z', uploadedBy: 'u-admin' },
  ];
  await page.getByRole('button', { name: 'Skrýt' }).click();
  await page.getByRole('button', { name: 'Zobrazit' }).click();
  await expect(history.getByRole('row')).toHaveCount(3);
  await expect(history.getByRole('row').nth(1)).toContainText('v2· aktuální');
  await expect(history.getByRole('row').nth(2)).not.toContainText('aktuální');
});
