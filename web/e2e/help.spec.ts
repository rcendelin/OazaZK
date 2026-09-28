import { expect, test } from '@playwright/test';
import { adminUser, mockApi, navigate, signInAsAdmin } from './mockApi';

const lossesComponent = {
  id: 'z', name: 'Ztráty vody', code: 'ZTRATY_VODY', startDate: '2023-11-01', allocationBasis: 'Metered', waterRole: 'Losses',
  active: true, note: null, currentMethod: 'Ratio', currentParticipants: 4,
};

test('help: Jak to funguje explains the new model and shows the current loss method to the admin', async ({ page }) => {
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/cost-components': () => [lossesComponent],
  });

  await signInAsAdmin(page);
  await navigate(page, '/jak-to-funguje');

  for (const title of [
    'Počáteční stavy — proč se nepřepočítává historie',
    'Nákladové složky a účast domů',
    'Voda a ztráty',
    'Přeplatek vodárny (kredit u dodavatele)',
    'Platby, zálohy a saldo',
    'Mezizávěrky a převod domu',
    'Pokladna',
  ]) {
    await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
  }
  await expect(page.getByTestId('current-loss-method')).toContainText('poměrem podle spotřeby vody domů');
  await expect(page.getByText('Portál nerekonstruuje minulost').first()).toBeVisible();
});

test('help: the ? next to a wizard step leads to its explanation', async ({ page }) => {
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
    '/houses': () => [],
    '/interim-closings': () => [],
    '/cost-components': () => [lossesComponent],
  });

  await signInAsAdmin(page);
  await navigate(page, '/mezizaverky');
  const help = page.getByRole('form', { name: 'Nová mezizávěrka' }).getByRole('link', { name: 'Nápověda k pojmu Mezizávěrka' });
  await expect(help).toHaveAttribute('title', /Zafixuje saldo k datu/);
  await help.click();

  await expect(page).toHaveURL(/\/jak-to-funguje#mezizaverka$/);
  await expect(page.locator('#mezizaverka')).toContainText('Zrušit lze jen poslední mezizávěrku');
});

test('help: the opening balances wizard has ? for every step', async ({ page }) => {
  await mockApi(page, {
    '/environment': () => ({ environment: 'test' }),
    '/auth/magic-link/verify': () => ({ token: 'e2e-jwt' }),
    '/auth/me': () => adminUser,
  });

  await signInAsAdmin(page);
  await navigate(page, '/admin/opening-balances');
  for (const label of ['Start účtování', 'Počáteční stav vodoměru', 'Podíl ve fondu', 'Kredit u dodavatele (přeplatek vodárny)', 'Převod domu']) {
    await expect(page.getByRole('link', { name: `Nápověda k pojmu ${label}`, exact: true })).toBeVisible();
  }
});
