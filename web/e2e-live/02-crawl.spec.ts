import { expect, go, loginAs, test, users } from './live';
import type { LiveUser } from './live';

/** Every page for every role: renders, no console error, no 5xx, forbidden pages are not reachable. */
const pages: { path: string; heading: RegExp; roles: ('Admin' | 'Accountant' | 'Member')[] }[] = [
  { path: '/dashboard', heading: /Přehled|Dobr|Vítej/i, roles: ['Admin', 'Accountant', 'Member'] },
  { path: '/readings', heading: /Odečty|Spotřeba/i, roles: ['Admin', 'Accountant', 'Member'] },
  { path: '/readings/list', heading: /Seznam odečtů/, roles: ['Admin'] },
  { path: '/readings/import', heading: /Import odečtů/, roles: ['Admin'] },
  { path: '/advances', heading: /Zálohy/, roles: ['Admin', 'Accountant', 'Member'] },
  { path: '/advances/import', heading: /Import/, roles: ['Admin'] },
  { path: '/saldo', heading: /^Platby$/, roles: ['Admin', 'Accountant', 'Member'] },
  { path: '/saldo-domu', heading: /Saldo/, roles: ['Admin', 'Accountant', 'Member'] },
  { path: '/pokladna', heading: /Pokladna/, roles: ['Admin', 'Accountant', 'Member'] },
  { path: '/mezizaverky', heading: /Mezizávěrky/, roles: ['Admin', 'Accountant'] },
  { path: '/voda', heading: /Voda a ztráty/, roles: ['Admin', 'Accountant'] },
  { path: '/naklady', heading: /Náklady/, roles: ['Admin', 'Accountant'] },
  { path: '/documents', heading: /Dokumenty/, roles: ['Admin', 'Accountant', 'Member'] },
  { path: '/finance', heading: /Hospodaření|Finance/i, roles: ['Admin', 'Accountant', 'Member'] },
  { path: '/jak-to-funguje', heading: /Jak to funguje/, roles: ['Admin', 'Accountant', 'Member'] },
  { path: '/navod', heading: /Návod pro správce/, roles: ['Admin', 'Accountant'] },
  { path: '/admin/houses', heading: /Domácnosti|Správa domácností/, roles: ['Admin'] },
  { path: '/admin/users', heading: /uživatel/i, roles: ['Admin'] },
  { path: '/admin/meters', heading: /vodoměr/i, roles: ['Admin'] },
  { path: '/admin/cost-components', heading: /Nákladové složky/, roles: ['Admin'] },
  { path: '/admin/opening-balances', heading: /Počáteční stavy/, roles: ['Admin'] },
  { path: '/admin/seed-import', heading: /Import počátečních dat/, roles: ['Admin'] },
  { path: '/admin/audit', heading: /Audit/, roles: ['Admin'] },
];

const roles: [string, LiveUser][] = [
  ['správce', users.admin],
  ['účetní', users.accountant],
  ['člen', users.member],
  ['člen bez domu', users.memberNoHouse],
];

for (const [label, user] of roles) {
  test(`procházení všech stránek — ${label}`, async ({ page }) => {
    await loginAs(page, user);
    for (const p of pages) {
      await go(page, p.path);
      if (p.roles.includes(user.role)) {
        await expect(page.getByRole('main').getByRole('heading', { level: 1 }).first(), `${p.path} nadpis`).toHaveText(p.heading);
        // wait for data: no spinner left and no error box
        await expect(page.locator('.animate-spin')).toHaveCount(0, { timeout: 30_000 });
        const errorBox = page.locator('.bg-danger-light').filter({ hasText: /chyba|nepodařilo|error/i });
        await expect(errorBox, `${p.path} bez chybové hlášky`).toHaveCount(0);
      } else {
        await expect(page.getByText('Přístup odepřen'), `${p.path} nesmí být dostupná pro roli ${user.role}`).toBeVisible();
      }
    }
  });
}

test('menu podle role: člen nevidí správu ani finanční nástroje', async ({ page }) => {
  await loginAs(page, users.member);
  const nav = page.getByRole('navigation').first();
  for (const hidden of ['Náklady', 'Voda a ztráty', 'Mezizávěrky', 'Návod pro správce', 'Import z banky', 'Domácnosti', 'Uživatelé', 'Audit změn', 'Import počátečních dat']) {
    await expect(nav.getByRole('link', { name: hidden, exact: true }), hidden).toHaveCount(0);
  }
  for (const shown of ['Přehled', 'Dokumenty', 'Jak to funguje']) {
    await expect(nav.getByRole('link', { name: shown, exact: true }).first(), shown).toBeVisible();
  }
});

test('mobilní zobrazení: hamburger menu a navigace', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await loginAs(page, users.admin);
  const burger = page.getByRole('button', { name: /menu/i }).first();
  await expect(burger).toBeVisible();
  await burger.click();
  await page.getByRole('link', { name: 'Dokumenty', exact: true }).first().click();
  await expect(page.getByRole('main').getByRole('heading', { level: 1 }).first()).toHaveText(/Dokumenty/);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow, 'vodorovné přetečení stránky na mobilu (px)').toBeLessThanOrEqual(1);
});
