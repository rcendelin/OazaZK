import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const houses = [
  { id: 'h1', name: 'RD1', address: '', contactPerson: 'Jana', email: '', isActive: true, dissolveOverpayment: false },
];
const meters = [
  { id: 'm1', meterNumber: 'V-001', name: '', type: 'Individual', houseId: 'h1', houseName: 'RD1', radioAddress: null, installationDate: '2020-01-01T00:00:00Z' },
];
const components = [
  { id: 'c1', name: 'Elektřina – vodárna', code: 'ELEKTRINA_VODARNA', startDate: '2023-11-01', allocationBasis: 'CostEntries', waterRole: 'None', active: true, note: null, currentMethod: 'Equal', currentParticipants: 4 },
];

test('opening balances: meter value suggested from readings (S8) is saved as an estimate', async ({ page }) => {
  let saved: Record<string, unknown> | null = null;
  const previews: (string | null)[] = [];
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
      // After saving, the refetch returns the new balance (with its key).
      return saved ? [{
        key: 'MeterReading|m1|h1|2023-11-01', type: 'MeterReading', houseId: 'h1', houseName: 'RD1', componentId: null, componentName: null,
        meterId: 'm1', meterNumber: 'V-001', ownershipPeriodId: 'h1|2023-11-01', date: '2023-11-01', value: 260.855, isEstimate: true,
        source: 'odhad', note: null, locked: false,
      }] : [];
    },
    '/readings/estimate': () => ({
      meterId: 'm1', targetDate: '2023-11-01T00:00:00Z', value: 260.855, isEstimate: true, method: 'Interpolated',
      note: 'Interpolace mezi 22. 5. 2023 (100 m³) a 19. 1. 2025 (700 m³), 163 z 608 dní.',
    }),
    '/opening-balances/component-credit-preview': (request) => {
      previews.push(new URL(request.url()).searchParams.get('value'));
      return {
      componentId: 'c1', date: '2023-11-01', method: 'Equal', total: -20000,
      shares: ['RD1', 'RD2', 'RD3', 'RD4'].map((houseName, i) => ({ houseId: `h${i + 1}`, houseName, weight: 1, amount: -5000 })),
      };
    },
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

  // „Uloženo.“ stays after the refetch (#21a: the row is not re-keyed when it gets its balance key).
  await expect(meterSection.getByRole('button', { name: 'Uložit změnu' })).toBeVisible();
  await expect(meterSection.getByRole('status')).toHaveText('Uloženo.');

  // S2: the waterworks credit preview splits −20 000 Kč among 4 houses — typed as the help text shows it
  // (typographic minus U+2212, space as thousands separator, #8).
  const credits = page.getByRole('region', { name: 'Kredit složky u dodavatele' });
  await credits.getByLabel('Hodnota Elektřina – vodárna').fill('\u221220\u00a0000');
  await credits.getByRole('button', { name: 'Náhled rozdělení' }).click();
  await expect(credits.getByRole('table', { name: 'Rozdělení Elektřina – vodárna' }).getByRole('row')).toHaveCount(4);
  expect(previews).toEqual(['-20000']);

  // Garbage is refused with a Czech message instead of being sent as 0.
  await credits.getByLabel('Hodnota Elektřina – vodárna').fill('abc');
  await credits.getByRole('button', { name: 'Náhled rozdělení' }).click();
  await expect(credits.getByRole('alert')).toContainText('zadejte číslo');
  expect(previews).toEqual(['-20000']);
});

test('opening balances after a house transfer: the new owner\'s value is editable, the previous owner\'s is listed read-only (#9)', async ({ page }) => {
  const puts: { url: string; body: Record<string, unknown> }[] = [];
  const posts: Record<string, unknown>[] = [];
  const oldBalance = {
    key: 'MeterReading|m1|h1|2023-11-01', type: 'MeterReading', houseId: 'h1', houseName: 'RD1', componentId: null, componentName: null,
    meterId: 'm1', meterNumber: 'V-001', ownershipPeriodId: 'h1|2023-11-01', date: '2023-11-01', value: 120.125, isEstimate: false,
    source: 'odečet', note: null, locked: true,
  };
  const oldFund = { ...oldBalance, key: 'FundShare|h1|h1|2023-11-01', type: 'FundShare', meterId: null, meterNumber: null, value: 1500 };
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/houses': () => houses,
    '/meters': () => meters,
    '/cost-components': () => [],
    '/ownership-periods': () => [
      { id: 'h1|2023-11-01', houseId: 'h1', houseName: 'RD1', ownerName: 'Jana', contact: null, validFrom: '2023-11-01', validTo: '2026-09-30' },
      { id: 'h1|2026-10-01', houseId: 'h1', houseName: 'RD1', ownerName: 'Petr', contact: null, validFrom: '2026-10-01', validTo: null },
    ],
    '/opening-balances': (request) => {
      if (request.method() === 'POST') {
        posts.push(request.postDataJSON());
        return {};
      }
      // The previous owner's values first (the old page took the first match).
      return [oldBalance, oldFund, {
        ...oldBalance, key: 'MeterReading|m1|h1|2026-10-01', ownershipPeriodId: 'h1|2026-10-01', date: '2026-10-01', value: 289.5,
        source: 'předávací protokol', locked: false,
      }];
    },
    '/opening-balances/MeterReading%7Cm1%7Ch1%7C2026-10-01': (request) => {
      puts.push({ url: request.url(), body: request.postDataJSON() });
      return {};
    },
  });

  await signInAsAdmin(page);
  await navigate(page, '/admin/opening-balances');

  const meterSection = page.getByRole('region', { name: 'Stavy vodoměrů' });
  await expect(meterSection.getByLabel('Hodnota RD1')).toHaveValue('289,5');
  await expect(meterSection.getByText('k 1. 10. 2026')).toBeVisible();
  await expect(meterSection.getByRole('list', { name: 'Dřívější hodnoty RD1' })).toHaveText('dřívější: 1. 11. 2023 — 120,125 m³ (uzavřeno)');

  await meterSection.getByLabel('Hodnota RD1').fill('290');
  await meterSection.getByRole('button', { name: 'Uložit změnu' }).click();
  await expect.poll(() => puts.length).toBe(1);
  expect(puts[0].body).toMatchObject({ type: 'MeterReading', meterId: 'm1', date: '2026-10-01', value: 290 });

  // The new owner has no fund share yet: a new value is dated to the start of their period, not 1. 11. 2023.
  const fundSection = page.getByRole('region', { name: 'Podíl ve fondu spolku' });
  await expect(fundSection.getByLabel('Hodnota RD1')).toHaveValue('');
  await expect(fundSection.getByText('k 1. 10. 2026')).toBeVisible();
  await expect(fundSection.getByRole('list', { name: 'Dřívější hodnoty RD1' })).toContainText(/1\s500 Kč \(uzavřeno\)/);
  await fundSection.getByLabel('Hodnota RD1').fill('0');
  await fundSection.getByLabel('Zdroj RD1').fill('převod domu');
  await fundSection.getByRole('button', { name: 'Uložit', exact: true }).click();
  await expect.poll(() => posts.length).toBe(1);
  expect(posts[0]).toMatchObject({ type: 'FundShare', houseId: 'h1', date: '2026-10-01', value: 0 });
});
