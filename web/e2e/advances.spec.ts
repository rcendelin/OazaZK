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
  const puts: unknown[] = [];
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/advance-settings': (request) => {
      if (request.method() === 'PUT') puts.push(request.postDataJSON());
      return { houseOverrides: { B: { waterAdvance: 800, electricityAdvance: 200, commonAdvance: 300 } } };
    },
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

  await rowA.getByRole('button', { name: 'Upravit' }).click();
  await page.getByLabel('Voda RD A').fill('1200');
  await rowA.getByRole('button', { name: 'Uložit' }).click();
  await expect(page.getByText('Záloha domu uložena.')).toBeVisible();

  expect(puts).toEqual([{
    houseOverrides: {
      B: { waterAdvance: 800, electricityAdvance: 200, commonAdvance: 300 },
      A: { waterAdvance: 1200, electricityAdvance: 200, commonAdvance: 300 },
    },
  }]);
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
