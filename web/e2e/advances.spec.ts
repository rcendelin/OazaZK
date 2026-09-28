import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const amounts = (water: number, electricity: number, common: number) => ({ water, electricity, common, total: water + electricity + common });

const calculation = {
  from: '2025-09-01',
  to: '2026-08-31',
  months: 12,
  houses: [
    { houseId: 'A', houseName: 'RD A', costsInPeriod: amounts(12000, 2400, 3600), recommended: amounts(1000, 200, 300), actual: amounts(1000, 200, 300), hasOverride: false },
    { houseId: 'B', houseName: 'RD B', costsInPeriod: amounts(6000, 2400, 3600), recommended: amounts(500, 200, 300), actual: amounts(800, 200, 300), hasOverride: true },
  ],
};

test('advances: recommended advance from the last 12 months of ledger costs; admin overrides one house', async ({ page }) => {
  const writes: { method: string; path: string; body: unknown }[] = [];
  const record = (request: import('@playwright/test').Request) => {
    if (request.method() !== 'GET') {
      writes.push({ method: request.method(), path: new URL(request.url()).pathname, body: request.postDataJSON() });
    }
    return { houseOverrides: {} };
  };
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/advance-settings': record,
    '/advance-settings/overrides/A': record,
    '/advance-settings/overrides/B': record,
    '/advance-settings/calculate': () => calculation,
  });

  await signInAsAdmin(page);
  await navigate(page, '/advances');

  await expect(page.getByRole('heading', { name: 'Zálohy', exact: true })).toBeVisible();
  await expect(page.getByText('Období: 1. 9. 2025 – 31. 8. 2026 (12 měsíců)')).toBeVisible();

  const table = page.getByRole('table', { name: 'Zálohy domů' });
  const rowA = table.getByRole('row', { name: /RD A/ });
  await expect(rowA.getByRole('cell').nth(4)).toHaveText(/18\s000/); // costs total
  await expect(rowA.getByRole('cell').nth(5)).toHaveText(/1\s500/); // recommended per month
  await expect(table.getByRole('row', { name: /RD B/ }).getByText('upraveno')).toBeVisible();
  await expect(rowA.getByText('upraveno')).toHaveCount(0);

  // #8: garbage is refused with a Czech message, nothing is saved.
  await rowA.getByRole('button', { name: 'Upravit' }).click();
  await page.getByLabel('Voda RD A').fill('12abc');
  await rowA.getByRole('button', { name: 'Uložit' }).click();
  await expect(page.getByText('Voda: zadejte číslo')).toBeVisible();
  expect(writes).toEqual([]);

  // #11: only house A is written (per-house endpoint) — a concurrent change of house B cannot be overwritten.
  await page.getByLabel('Voda RD A').fill('1 200');
  await rowA.getByRole('button', { name: 'Uložit' }).click();
  await expect(page.getByText('Záloha domu uložena.')).toBeVisible();
  expect(writes).toEqual([
    { method: 'PUT', path: '/api/advance-settings/overrides/A', body: { waterAdvance: 1200, electricityAdvance: 200, commonAdvance: 300 } },
  ]);

  await table.getByRole('row', { name: /RD B/ }).getByRole('button', { name: 'Zrušit úpravu' }).click();
  await expect(page.getByText('Záloha domu vrácena na doporučenou.')).toBeVisible();
  expect(writes.slice(1)).toEqual([{ method: 'DELETE', path: '/api/advance-settings/overrides/B', body: null }]);
});

test('platby: page is titled Platby, points to Saldo domu and has no opening-balance option', async ({ page }) => {
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/advance-settings/calculate': () => calculation,
    '/houses': () => [{ id: 'A', name: 'RD A', isActive: true }],
  });

  await signInAsAdmin(page);
  const planLoaded = page.waitForResponse('**/api/advance-settings/calculate');
  await navigate(page, '/saldo');

  await expect(page.getByRole('heading', { name: 'Platby', exact: true })).toBeVisible();
  // The Hospodaření menu is expanded on its child page: renamed item, no old-model entries.
  await expect(page.getByRole('link', { name: 'Platby', exact: true }).first()).toBeVisible();
  await expect(page.getByRole('link', { name: 'Saldo a platby' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Vyúčtování' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Přehled faktur' })).toHaveCount(0);
  await expect(page.getByRole('main').getByRole('link', { name: 'Saldo domu' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Počáteční stav' })).toHaveCount(0);

  await planLoaded;
  await page.locator('select').first().selectOption({ label: 'RD A' });
  await page.getByRole('button', { name: 'Předvyplnit předepsanou zálohu' }).click();
  await expect(page.getByRole('button', { name: /Uložit \(1\s500 Kč\)/ })).toBeVisible();
});

test('platby: after saving an advance the form keeps house, year and date and moves to the next month (#20); garbage is refused (#8)', async ({ page }) => {
  const posted: Record<string, unknown>[] = [];
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/advance-settings/calculate': () => calculation,
    '/houses': () => [{ id: 'A', name: 'RD A', isActive: true }, { id: 'B', name: 'RD B', isActive: true }],
    '/advances': (request) => {
      if (request.method() === 'POST') {
        posted.push(request.postDataJSON());
        return {};
      }
      return [];
    },
  });

  await signInAsAdmin(page);
  await navigate(page, '/saldo');
  await expect(page.getByRole('heading', { name: 'Platby', exact: true })).toBeVisible();

  await page.locator('select').first().selectOption({ label: 'RD B' });
  await page.getByLabel('Rok').fill('2026');
  await page.getByLabel('Měsíc').fill('12');
  await page.getByLabel('Datum').fill('2026-12-05');

  await page.getByLabel('Voda (Kč)').fill('abc');
  await page.getByRole('button', { name: /^Uložit/ }).click();
  await expect(page.getByText('Voda: zadejte číslo')).toBeVisible();
  expect(posted).toEqual([]);

  await page.getByLabel('Voda (Kč)').fill('1 000,50');
  await page.getByRole('button', { name: /^Uložit/ }).click();
  await expect(page.getByText('Záloha za 2026-12 uložena.')).toBeVisible();
  expect(posted).toHaveLength(1);
  expect(posted[0]).toMatchObject({ houseId: 'B', year: 2026, month: 12, waterAmount: 1000.5, electricityAmount: 0, commonAmount: 0 });

  // The form stayed mounted during the refetch: same house and date, the next month (over the year end).
  await expect(page.locator('select').first()).toHaveValue('B');
  await expect(page.getByLabel('Datum')).toHaveValue('2026-12-05');
  await expect(page.getByLabel('Rok')).toHaveValue('2027');
  await expect(page.getByLabel('Měsíc')).toHaveValue('1');
  await expect(page.getByLabel('Voda (Kč)')).toHaveValue('');
});

test('dashboard (member): saldo card shows the ledger saldo with X1 sign and links to Saldo domu', async ({ page }) => {
  const member = { ...adminUser, id: 'u-member', role: 'Member', houseId: 'A' };
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => member,
    '/ledger/houses/A': () => ({
      houseId: 'A', houseName: 'RD A', from: '2023-11-01', to: '2026-08-31', ownershipPeriod: null, ownershipPeriods: [],
      opening: 0, payments: 1000, costs: -1250, saldo: -250, items: [],
    }),
  });

  await signInAsAdmin(page);

  const card = page.getByRole('link', { name: 'Saldo domu' }).filter({ hasText: 'Nedoplatek' });
  await expect(card).toContainText('250 Kč');
  await card.click();
  await page.waitForURL('**/saldo-domu');
});
