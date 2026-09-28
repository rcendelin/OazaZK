import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const houses = [
  { id: 'B', name: 'RD B', address: '', contactPerson: 'Původní B', email: '', isActive: true, dissolveOverpayment: false },
];

test('house transfer (S4): preview shows the old owner closing saldo, transfer posts the new owner start', async ({ page }) => {
  let transfer: Record<string, unknown> | null = null;
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/houses': () => houses,
    '/meters': () => [],
    '/cost-components': () => [],
    '/ownership-periods': () => [{ id: 'B|2023-11-01', houseId: 'B', houseName: 'RD B', ownerName: 'Původní B', contact: null, validFrom: '2023-11-01', validTo: null }],
    '/opening-balances': () => [],
    '/houses/B/transfer-preview': () => ({
      houseId: 'B', houseName: 'RD B', transferDate: '2026-03-15', closingDate: '2026-03-14', currentOwnerName: 'Původní B', currentOwnerFrom: '2023-11-01',
      closingSaldo: 650, closingPayments: 1000, closingCosts: 350, meterId: 'mB', meterNumber: 'V-B',
      suggestedMeterValue: 1234.516, suggestedMeterNote: 'Odhad lineární interpolací…', problems: [],
    }),
    '/houses/B/transfer': (request) => {
      transfer = request.postDataJSON();
      return { closingId: '2026-03-14|House|B', closingSaldo: 650, newOwnershipPeriodId: 'B|2026-03-15' };
    },
  });

  await signInAsAdmin(page);
  await navigate(page, '/admin/opening-balances');

  const section = page.getByRole('region', { name: 'Převod domu' });
  await section.getByRole('combobox').first().selectOption({ label: 'RD B' });
  await section.getByLabel('Nový vlastník od').fill('2026-03-15');
  await section.getByRole('button', { name: 'Náhled dopadů' }).click();

  const preview = section.getByLabel('Náhled převodu');
  await expect(preview).toContainText('14. 3. 2026');
  await expect(preview).toContainText('650,00 Kč přeplatek');

  const form = section.getByRole('form', { name: 'Údaje nového vlastníka' });
  await expect(form.getByLabel('Stav vodoměru V-B (m³)')).toHaveValue('1234,516');
  await form.getByLabel('Stav vodoměru V-B (m³)').fill('1234,567');
  await form.getByLabel('Nový vlastník', { exact: true }).fill('Noví vlastníci');
  await form.getByRole('button', { name: 'Převést dům' }).click();

  await expect(section.getByRole('status')).toContainText('Závěrečné saldo původního vlastníka: 650,00 Kč (přeplatek)');
  expect(transfer).toMatchObject({ transferDate: '2026-03-15', newOwnerName: 'Noví vlastníci', meterValue: 1234.567, fundShare: 0, updateHouseContact: true });
});
