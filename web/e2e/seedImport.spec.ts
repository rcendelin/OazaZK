import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const report = (over: Record<string, unknown>) => ({
  applied: false,
  canApply: true,
  from: '2023-11-01',
  today: '2026-12-31',
  files: [
    { file: 'houses.csv', uploaded: true, rows: 2, created: 2, unchanged: 0, conflicts: 0, errors: 0 },
    { file: 'meters.csv', uploaded: false, rows: 0, created: 0, unchanged: 0, conflicts: 0, errors: 0 },
  ],
  issues: [],
  houses: [
    { houseName: 'RD A', opening: 1000, payments: 0, costs: 664, saldo: 5336 },
    { houseName: 'RD E', opening: 0, payments: 0, costs: 184, saldo: -184 },
  ],
  components: [{ componentName: 'Osvětlení', allocated: 1840, houses: 1840, matches: true, warnings: [] }],
  ...over,
});

test('seed import: dry run shows the report, apply writes after confirmation', async ({ page }) => {
  const bodies: Record<string, unknown>[] = [];
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/seed-import/dry-run': (request) => {
      bodies.push(request.postDataJSON());
      return report({});
    },
    '/seed-import/apply': (request) => {
      bodies.push(request.postDataJSON());
      return report({ applied: true, canApply: false });
    },
  });

  await signInAsAdmin(page);
  await navigate(page, '/admin/seed-import');
  await expect(page.getByRole('heading', { name: 'Import počátečních dat' })).toBeVisible();

  await page.getByLabel('Soubory CSV podle šablon').setInputFiles({
    name: 'Houses.csv',
    mimeType: 'text/csv',
    buffer: Buffer.from('name;address;contact_person;email;active\nRD A;;;;ano\n', 'utf-8'),
  });
  await expect(page.getByRole('list', { name: 'Nahrané soubory' })).toContainText('houses.csv');
  await page.getByRole('button', { name: 'Zkontrolovat nanečisto' }).click();

  const result = page.getByRole('region', { name: 'Report importu' });
  await expect(result.getByText('Zkouška prošla — data lze zapsat.')).toBeVisible();
  await expect(result.getByRole('table', { name: 'Soubory' })).toContainText('nenahráno');
  await expect(result.getByRole('table', { name: 'Salda domů po importu' }).getByRole('row', { name: /RD E/ })).toContainText(/−?-184,00\sKč/);
  expect(bodies[0]).toEqual({ files: { 'houses.csv': 'name;address;contact_person;email;active\nRD A;;;;ano\n' } });

  await result.getByRole('button', { name: 'Zapsat' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Zapsat' }).click();
  await expect(result.getByText('Zapsáno. Opakovaný import už nic nezmění.')).toBeVisible();
  await expect(result.getByRole('button', { name: 'Zapsat' })).toBeDisabled();
  expect(bodies).toHaveLength(2);
});

test('seed import: conflicts block writing', async ({ page }) => {
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/seed-import/dry-run': () => report({
      canApply: false,
      issues: [{ file: 'cost_entries.csv', line: 4, severity: 'konflikt', message: 'Náklad S3 už existuje s jinými hodnotami (částka: 1 840 → 1 900)' }],
    }),
  });

  await signInAsAdmin(page);
  await navigate(page, '/admin/seed-import');
  await page.getByLabel('Soubory CSV podle šablon').setInputFiles({ name: 'cost_entries.csv', mimeType: 'text/csv', buffer: Buffer.from('ref;component\n') });
  await page.getByRole('button', { name: 'Zkontrolovat nanečisto' }).click();

  const result = page.getByRole('region', { name: 'Report importu' });
  await expect(result.getByText('Nelze zapsat — opravte chyby a konflikty')).toBeVisible();
  await expect(result.getByRole('table', { name: 'Chyby a konflikty' })).toContainText('1 840 → 1 900');
  await expect(result.getByRole('button', { name: 'Zapsat' })).toBeDisabled();
});
