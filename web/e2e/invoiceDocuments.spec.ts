import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const component = {
  id: 'c-vodarna', name: 'Elektřina – vodárna', code: 'ELEKTRINA_VODARNA', startDate: '2023-11-01',
  allocationBasis: 'CostEntries', waterRole: 'None', active: true, note: null, currentMethod: 'Equal', currentParticipants: 4,
};
const invoice = {
  id: 'doc-1', category: 'faktury', name: 'PRE vyúčtování 2026', fileSizeBytes: 1000, contentType: 'application/pdf',
  uploadedAt: '2026-09-20T08:00:00Z', uploadedBy: 'u-admin', componentId: 'c-vodarna',
};
const signIn = {
  '/environment': () => ({ environment: 'test' }),
  '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
  '/auth/me': () => adminUser,
};

test('invoices: five PDFs upload at once into „Faktury a vyúčtování“ with the component', async ({ page }) => {
  const uploads: string[] = [];
  await mockApi(page, {
    ...signIn,
    '/cost-components': () => [component],
    '/documents': (request) => {
      if (request.method() === 'POST') {
        uploads.push(request.url());
        return invoice;
      }
      return [];
    },
  });

  await signInAsAdmin(page);
  await navigate(page, '/documents');
  await page.getByText('Nahrát faktury a vyúčtování (více souborů najednou)').click();
  const bulk = page.getByLabel('Hromadné nahrání faktur');
  await bulk.locator('input[type=file]').setInputFiles(
    [1, 2, 3, 4, 5].map((i) => ({ name: `faktura-${i}.pdf`, mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4') })),
  );
  await bulk.getByRole('combobox').selectOption({ label: 'Elektřina – vodárna' });
  await bulk.getByRole('button', { name: 'Nahrát 5' }).click();

  await expect(page.getByLabel('Výsledek nahrání').getByRole('listitem')).toHaveCount(5);
  expect(uploads).toHaveLength(5);
  expect(uploads.every((u) => u.includes('category=faktury') && u.includes('componentId=c-vodarna'))).toBe(true);
  expect(uploads[0]).toContain('name=faktura-1');
});

test('invoices: booking an unaccounted document prefills it and it leaves the list', async ({ page }) => {
  let booked: Record<string, unknown> | null = null;
  await mockApi(page, {
    ...signIn,
    '/cost-components': () => [component],
    '/documents': () => [invoice],
    '/documents/unaccounted': () => (booked ? [] : [{ document: invoice, componentName: 'Elektřina – vodárna' }]),
    '/cost-components/c-vodarna/entries': (request) => {
      if (request.method() === 'POST') {
        booked = request.postDataJSON();
        return { id: 'e1' };
      }
      return [];
    },
  });

  await signInAsAdmin(page);
  await navigate(page, '/naklady');
  const unaccounted = page.getByRole('region', { name: 'Dokumenty bez zaúčtování' });
  await expect(unaccounted).toContainText('PRE vyúčtování 2026');
  await unaccounted.getByRole('button', { name: 'Zaúčtovat' }).click();

  const form = page.getByRole('form', { name: 'Přidat náklad' });
  await expect(form.getByLabel('Doklad (PDF v Dokumentech)')).toHaveValue('doc-1');
  await form.getByLabel('Období od').fill('2026-01-01');
  await form.getByLabel('do', { exact: true }).fill('2026-06-30');
  await form.getByLabel('Částka (Kč)').fill('1840');
  await form.getByRole('button', { name: 'Přidat' }).click();

  await expect.poll(() => booked).not.toBeNull();
  expect(booked).toMatchObject({ documentId: 'doc-1', amount: 1840, periodFrom: '2026-01-01', periodTo: '2026-06-30' });
  await expect(page.getByRole('region', { name: 'Dokumenty bez zaúčtování' })).toHaveCount(0);
});
