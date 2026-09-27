import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const house = (id: string, name: string, consumptionM3: number, waterCost: number, lossCost: number, isEstimate = false) =>
  ({ houseId: id, houseName: name, consumptionM3, isEstimate, waterCost, lossCost });
const houses = [house('a', 'RD A', 40, 4000, 444.44), house('b', 'RD B', 30, 3000, 333.33), house('c', 'RD C', 15, 1500, 166.67), house('d', 'RD D', 5, 500, 55.56, true)];

test('water: S1 interval shows loss, price, ratio split and the estimate mark', async ({ page }) => {
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/water-settlement': () => ({
      from: '2026-01-01', to: '2026-06-30', consumptionComponentName: 'Voda PVK', lossComponentName: 'Ztráty vody',
      intervals: [{
        from: '2026-01-01', to: '2026-06-30', days: 181, mainConsumptionM3: 100, housesConsumptionM3: 90, lossM3: 10,
        pricePerM3: 100, invoicedAmount: 10000, invoicedM3: 100, waterCost: 9000, lossCost: 1000, difference: 0, allocated: true,
        warnings: [], lossSegments: [{ from: '2026-01-01', to: '2026-06-30', days: 181, method: 'Ratio', amount: 1000, participants: 4 }],
        houses,
      }],
      totals: houses,
    }),
  });

  await signInAsAdmin(page);
  await navigate(page, '/voda');
  await expect(page.getByRole('heading', { name: 'Voda a ztráty' })).toBeVisible();

  const interval = page.getByRole('region', { name: 'Úsek 1. 1. 2026 – 30. 6. 2026' });
  await expect(interval.getByText('10 m³', { exact: true })).toBeVisible();
  await expect(interval.getByText('100,00 Kč', { exact: true })).toBeVisible();
  await expect(interval.getByText(/poměrem mezi 4 domů/)).toBeVisible();
  await expect(interval.getByRole('row', { name: /RD A/ })).toContainText('444,44 Kč');
  await expect(interval.getByRole('row', { name: /RD D/ }).getByTitle('Hraniční odečet je dopočtený (odhad)')).toBeVisible();
});
