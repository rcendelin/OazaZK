import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const houses = [
  { id: 'h1', name: 'RD1', address: '', contactPerson: 'Jana', email: '', isActive: true, dissolveOverpayment: false },
];
const meters = [
  { id: 'm1', meterNumber: 'V-001', name: '', type: 'Individual', houseId: 'h1', houseName: 'RD1', radioAddress: null, installationDate: '2020-01-01T00:00:00Z' },
];
const components = [
  { id: 'c1', name: 'Elektřina – vodárna', code: 'ELEKTRINA_VODARNA', startDate: '2023-11-01', allocationBasis: 'CostEntries', active: true, note: null, currentMethod: 'Equal', currentParticipants: 4 },
];

test('opening balances: meter value suggested from readings (S8) is saved as an estimate', async ({ page }) => {
  let saved: Record<string, unknown> | null = null;
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/houses': () => houses,
    '/meters': () => meters,
    '/cost-components': () => components,
    '/ownership-periods': () => [{ id: 'h1|2023-11-01', houseId: 'h1', houseName: 'RD1', ownerName: 'Jana', contact: null, validFrom: '2023-11-01', validTo: null }],
    '/opening-balances': (request) => {
      if (request.method() === 'POST') {
        saved = request.postDataJSON();
        return { key: 'MeterReading|m1|h1|2023-11-01' };
      }
      return [];
    },
    '/readings/estimate': () => ({
      meterId: 'm1', targetDate: '2023-11-01T00:00:00Z', value: 260.855, isEstimate: true, method: 'Interpolated',
      note: 'Interpolace mezi 22. 5. 2023 (100 m³) a 19. 1. 2025 (700 m³), 163 z 608 dní.',
    }),
    '/opening-balances/component-credit-preview': () => ({
      componentId: 'c1', date: '2023-11-01', method: 'Equal', total: -20000,
      shares: ['RD1', 'RD2', 'RD3', 'RD4'].map((houseName, i) => ({ houseId: `h${i + 1}`, houseName, weight: 1, amount: -5000 })),
    }),
  });

  await signInAsAdmin(page);
  await navigate(page, '/admin/opening-balances');
  await expect(page.getByRole('heading', { name: 'Počáteční stavy', exact: true })).toBeVisible();

  const meterSection = page.getByRole('region', { name: 'Stavy vodoměrů' });
  await meterSection.getByRole('button', { name: 'Navrhnout z odečtů' }).click();
  await expect(meterSection.getByLabel('Hodnota RD1')).toHaveValue('260,855');
  await expect(meterSection.getByRole('checkbox')).toBeChecked();
  await meterSection.getByRole('button', { name: 'Uložit' }).click();

  await expect.poll(() => saved).not.toBeNull();
  expect(saved).toMatchObject({ type: 'MeterReading', meterId: 'm1', date: '2023-11-01', value: 260.855, isEstimate: true });

  // S2: the waterworks credit preview splits −20 000 Kč among 4 houses.
  const credits = page.getByRole('region', { name: 'Kredit složky u dodavatele' });
  await credits.getByLabel('Hodnota Elektřina – vodárna').fill('-20000');
  await credits.getByRole('button', { name: 'Náhled rozdělení' }).click();
  await expect(credits.getByRole('table', { name: 'Rozdělení Elektřina – vodárna' }).getByRole('row')).toHaveCount(4);
});
