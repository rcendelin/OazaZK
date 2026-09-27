import { readdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { Page } from '@playwright/test';
import { RUN, api, expect, go, loginAs, test, token, users } from './live';
import { TODAY, activeHouses, componentByCode, download, isXlsx, json } from './data';

/**
 * X4 audit, T12 nápověda (Jak to funguje, Návod pro správce, „?“), robustnost (dvojklik, dlouhé texty, hranice dat,
 * zpět/vpřed, vypršelé přihlášení, souběžná úprava) a T13 import počátečních dat — jen nanečisto (nic se nezapisuje).
 */
const TAG = `${RUN}-${Date.now().toString(36).slice(-4)}`;
const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
interface Audit { id: string; timestamp: string; userId: string; userName: string | null; action: string; entityType: string; entityId: string; reason: string | null }

test.describe.serial('audit změn (X4)', () => {
  test('audit obsahuje zápisy nového modelu s aktérem = přihlášený správce; filtr podle typu v UI', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const me = await json<{ name: string }>(admin, 'auth/me');
    const entries = await json<Audit[]>(admin, `audit-log?from=2026-09-01&to=${TODAY}`);
    const types = new Set(entries.filter((e) => e.userId === users.admin.id).map((e) => e.entityType));
    for (const t of ['CostComponent', 'ComponentAllocationRule', 'Participation', 'OpeningBalance', 'OwnershipPeriod', 'CostEntry', 'CashBookEntry', 'InterimClosing'])
      expect(types.has(t), `audit typu ${t}`).toBe(true);
    const corrections = entries.filter((e) => e.action === 'Correction');
    expect(corrections.length).toBeGreaterThan(0);
    expect(corrections.every((e) => (e.reason ?? '').length > 0)).toBe(true);
    const deletedClosing = entries.find((e) => e.entityType === 'InterimClosing' && e.action === 'Delete');
    expect(deletedClosing?.reason).toBeTruthy();

    await loginAs(page, users.admin);
    await go(page, '/admin/audit');
    await expect(page.getByRole('heading', { name: 'Audit změn' })).toBeVisible();
    await page.getByLabel('Typ záznamu').fill('InterimClosing');
    await page.getByRole('button', { name: 'Zobrazit' }).click();
    const rows = page.getByRole('table').locator('tbody tr');
    await expect(rows.first()).toContainText('InterimClosing');
    await expect(rows.filter({ hasNotText: 'InterimClosing' })).toHaveCount(0);
    await expect(rows.first()).toContainText(me.name);
    await rows.filter({ hasText: 'Smazání' }).first().getByText('Před / po').click();
    await expect(rows.filter({ hasText: 'Smazání' }).first().locator('pre').first()).toContainText('{');
    // range longer than 2 years → error, not a crash
    await page.getByLabel('Od').fill('2020-01-01');
    await page.getByRole('button', { name: 'Zobrazit' }).click();
    await expect(page.locator('div.bg-danger-light')).toBeVisible();
  });

  test('platby a opravy odečtů jsou v auditu (kdo smazal platbu, kdo přepsal odečet)', async ({ playwright }) => {
    const admin = await api(playwright, users.admin);
    const entries = await json<Audit[]>(admin, `audit-log?from=2026-09-01&to=${TODAY}`);
    const types = new Set(entries.map((e) => e.entityType));
    test.info().annotations.push({ type: 'evidence', description: `typy v auditu: ${[...types].sort().join(', ')}` });
    expect([...types].some((t) => /Advance|Payment/i.test(t)), 'platby').toBe(true);
    expect(entries.some((e) => e.entityType === 'MeterReading' && e.action !== 'Create'), 'oprava odečtu').toBe(true);
  });
});

test.describe.serial('nápověda (T12)', () => {
  test('Jak to funguje: aktuální metoda ztrát je vidět pro správce (poměrem od 2026), člen ji nevidí', async ({ page }) => {
    await loginAs(page, users.admin);
    await go(page, '/jak-to-funguje');
    await expect(page.getByTestId('current-loss-method')).toContainText('poměrem podle spotřeby vody domů');
    await expect(page.getByTestId('current-loss-method')).toContainText('Ztráty vody');
    await page.getByRole('link', { name: 'Slovník pojmů' }).click();
    await expect(page.getByRole('heading', { name: 'Slovník pojmů' })).toBeInViewport();

    const memberPage = await page.context().browser()!.newPage();
    await loginAs(memberPage, users.member);
    await go(memberPage, '/jak-to-funguje');
    await expect(memberPage.getByRole('heading', { name: 'Jak to funguje' })).toBeVisible();
    await expect(memberPage.getByTestId('current-loss-method')).toHaveCount(0);
    await memberPage.close();
  });

  test('„?“ u pojmu vede na výklad v Jak to funguje a posune na něj', async ({ page }) => {
    await loginAs(page, users.admin);
    await go(page, '/mezizaverky');
    await page.getByRole('link', { name: 'Nápověda k pojmu Mezizávěrka' }).click();
    await expect(page).toHaveURL(/\/jak-to-funguje#mezizaverka$/);
    await expect(page.locator('#mezizaverka')).toBeInViewport();
    await go(page, '/admin/opening-balances');
    await expect(page.getByRole('heading', { name: 'Stavy vodoměrů' })).toBeVisible({ timeout: 30_000 });
    const terms = page.getByRole('link', { name: /^Nápověda k pojmu / });
    expect(await terms.count()).toBeGreaterThanOrEqual(5);
    await terms.nth(2).click();
    await expect(page).toHaveURL(/\/jak-to-funguje#\w+$/);
    const id = new URL(page.url()).hash.slice(1);
    await expect(page.locator(`[id="${id}"]`)).toBeInViewport();
  });

  test('Návod pro správce: všechny obrázky se načtou, odkazy „Otevřít stránku →“ vedou na existující stránky', async ({ page }) => {
    await loginAs(page, users.admin);
    await go(page, '/navod');
    await expect(page.getByRole('heading', { name: 'Návod pro správce' })).toBeVisible();
    const images = page.locator('main img');
    const n = await images.count();
    expect(n).toBeGreaterThan(3);
    for (let i = 0; i < n; i++) {
      const img = images.nth(i);
      await img.scrollIntoViewIfNeeded();
      await expect.poll(() => img.evaluate((el: HTMLImageElement) => el.complete && el.naturalWidth > 0), { message: await img.getAttribute('src') ?? '' }).toBe(true);
    }
    const links = page.getByRole('link', { name: 'Otevřít stránku →' });
    const hrefs = [...new Set(await links.evaluateAll((els) => els.map((e) => e.getAttribute('href') ?? '')))];
    for (const href of hrefs) {
      await go(page, href);
      await expect(page.getByText('Stránka nenalezena')).toHaveCount(0);
      await expect(page.getByRole('main').getByRole('heading', { level: 1 }).first()).toBeVisible();
    }
  });
});

test.describe.serial('robustnost', () => {
  test('dvojklik na „Zapsat“ v pokladně a „Přidat“ v nákladech vytvoří jen jeden záznam', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    await loginAs(page, users.admin);
    await go(page, '/pokladna');
    const form = page.getByRole('form', { name: 'Nový záznam pokladny' });
    await form.getByLabel('Druh').selectOption('Deposit');
    await form.getByLabel('Částka (Kč)').fill('10');
    await form.getByLabel('Co', { exact: true }).fill(`dvojklik ${TAG}`);
    await form.getByRole('button', { name: 'Zapsat' }).dblclick();
    await expect(page.getByRole('table', { name: 'Záznamy pokladny' }).getByText(`dvojklik ${TAG}`).first()).toBeVisible();
    await page.waitForTimeout(2000);
    const book = await json<{ entries: { id: string; description: string }[] }>(admin, 'cash-book');
    const mine = book.entries.filter((e) => e.description === `dvojklik ${TAG}`);
    for (const e of mine) await admin.post(`cash-book/${e.id}/storno`, { data: { reason: 'cleanup' } });
    expect(mine.length, 'pokladna: počet záznamů po dvojkliku').toBe(1);

    const osv = (await componentByCode(admin, 'OSVETLENI'))!;
    await go(page, '/naklady');
    await page.getByRole('tab', { name: osv.name, exact: true }).click();
    const cost = page.getByRole('form', { name: 'Přidat náklad' });
    await cost.getByLabel('Druh').selectOption('OneOff');
    await cost.getByLabel('Období od').fill('2026-09-10');
    await cost.getByLabel('Částka (Kč)').fill('10');
    await cost.getByLabel('Poznámka').fill(`dvojklik ${TAG}`);
    await cost.getByRole('button', { name: 'Přidat' }).dblclick();
    await page.waitForTimeout(3000);
    const entries = (await json<{ id: string; note: string | null }[]>(admin, `cost-components/${osv.id}/entries?from=2026-09-10&to=2026-09-10`)).filter((e) => e.note === `dvojklik ${TAG}`);
    for (const e of entries) await admin.delete(`cost-components/${osv.id}/entries/${e.id}?reason=cleanup`);
    expect(entries.length, 'náklady: počet záznamů po dvojkliku').toBe(1);
  });

  test('velmi dlouhé texty a nesmyslná data: žádná chyba serveru (400 s hláškou, ne 500)', async ({ playwright }) => {
    const admin = await api(playwright, users.admin);
    const osv = (await componentByCode(admin, 'OSVETLENI'))!;
    const long = 'Ř'.repeat(20_000);
    const checks: Record<string, number> = {};
    const res1 = await admin.post(`cost-components/${osv.id}/entries`, { data: { type: 'OneOff', periodFrom: '2026-09-11', periodTo: '2026-09-11', amount: 1, paidFrom: 'Bank', note: long, supplier: long } });
    checks.longNote = res1.status();
    if (res1.status() === 201) await admin.delete(`cost-components/${osv.id}/entries/${(await res1.json()).id}?reason=cleanup`);
    checks.feb30 = (await admin.post(`cost-components/${osv.id}/entries`, { data: { type: 'OneOff', periodFrom: '2025-02-30', periodTo: '2025-02-30', amount: 1, paidFrom: 'Bank' } })).status();
    const leap = await admin.post(`cost-components/${osv.id}/entries`, { data: { type: 'OneOff', periodFrom: '2024-02-29', periodTo: '2024-02-29', amount: 1, paidFrom: 'Bank', note: TAG } });
    checks.leapDay = leap.status();
    if (leap.status() === 201) await admin.delete(`cost-components/${osv.id}/entries/${(await leap.json()).id}?reason=cleanup`);
    checks.hugeAmount = (await admin.post('advances/payout', { data: { houseId: users.member.houseId, amount: 1e30, paymentDate: '2026-09-01T00:00:00Z' } })).status();
    checks.badJson = (await admin.fetch('cash-book', { method: 'POST', data: '{"date": ', headers: { 'Content-Type': 'application/json' } })).status();
    checks.ledgerBadRange = (await admin.get('ledger/overview?from=2026-12-31&to=2026-01-01')).status();
    checks.waterTooLong = (await admin.get('water-settlement?from=2010-01-01&to=2026-09-27')).status();
    checks.segmentsBadDate = (await admin.get(`cost-components/${osv.id}/segments?from=abc&to=2026-01-01`)).status();
    test.info().annotations.push({ type: 'evidence', description: JSON.stringify(checks) });
    // cleanup of an accepted absurd payout
    if (checks.hugeAmount < 300) {
      const ps = await json<{ rowKey: string; amount: number }[]>(admin, `advances?houseId=${users.member.houseId}`);
      for (const p of ps.filter((x) => x.amount > 1e20)) await admin.delete(`advances/${users.member.houseId}/${encodeURIComponent(p.rowKey)}`);
    }
    expect(checks.leapDay).toBe(201);
    expect(checks.feb30).toBe(400);
    // BUG: poznámka/dodavatel 20 000 znaků → 500 (limit vlastnosti Table Storage, chybí validace délky);
    // částka 1e30 → 500 (přetečení decimal při deserializaci) — obojí má být 400 s hláškou.
    test.fail(checks.longNote >= 500 || checks.hugeAmount >= 500, 'chyba serveru místo validace');
    const server = Object.entries(checks).filter(([, s]) => s >= 500);
    expect(server, JSON.stringify(checks)).toEqual([]);
  });

  test('zpět/vpřed v prohlížeči mezi stránkami aplikace', async ({ page }) => {
    await loginAs(page, users.admin);
    await go(page, '/naklady');
    await expect(page.getByRole('heading', { name: 'Náklady', exact: true })).toBeVisible();
    await page.getByRole('tab').nth(1).click(); // ?component=… in the URL
    const withComponent = page.url();
    await go(page, '/pokladna');
    await expect(page.getByRole('heading', { name: 'Pokladna' })).toBeVisible();
    await page.goBack();
    await expect(page).toHaveURL(withComponent);
    await expect(page.getByRole('heading', { name: 'Náklady', exact: true })).toBeVisible();
    await expect(page.getByRole('tab').nth(1)).toHaveAttribute('aria-selected', 'true');
    await page.goForward();
    await expect(page.getByRole('heading', { name: 'Pokladna' })).toBeVisible();
  });

  test('vypršelé přihlášení: aplikace pošle uživatele na přihlášení (ne prázdné stránky s chybou)', async ({ page, allowProblems }) => {
    allowProblems(/HTTP 401/);
    test.setTimeout(150_000);
    // the API tolerates 1 minute of clock skew (JwtService ClockSkew) — the token is refused ~65 s after it expires
    const jwt = token(users.admin, { expiresIn: 5 });
    await page.route('**/api/auth/magic-link/verify', (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ token: jwt }) }));
    await page.goto(`/auth/verify?token=e2e&email=${encodeURIComponent(users.admin.email)}`);
    await page.waitForURL('**/dashboard', { timeout: 60_000 });
    await page.waitForTimeout(70_000);
    await go(page, '/pokladna');
    await expect(page).toHaveURL(/\/login/, { timeout: 15_000 });
  });

  test('souběžná úprava záloh dvěma správci nesmí tiše přepsat cizí změnu (PUT celé mapy přepisů)', async ({ browser, playwright }) => {
    const admin = await api(playwright, users.admin);
    const houses = await activeHouses(admin);
    const [a, b] = houses.filter((h) => h.id !== users.member.houseId);
    const settings = await json<{ houseOverrides: Record<string, unknown> }>(admin, 'advance-settings');
    const original = settings.houseOverrides;
    const open = async () => {
      const ctx = await browser.newContext();
      const p = await ctx.newPage();
      await loginAs(p, users.admin);
      await go(p, '/advances');
      await expect(p.getByRole('table', { name: 'Zálohy domů' })).toBeVisible();
      return p;
    };
    const setOverride = async (p: Page, name: string, water: string) => {
      const row = p.getByRole('table', { name: 'Zálohy domů' }).getByRole('row').filter({ hasText: name });
      await row.getByRole('button', { name: 'Upravit' }).click();
      await row.getByLabel(`Voda ${name}`).fill(water);
      await row.getByRole('button', { name: 'Uložit' }).click();
      await expect(p.getByText('Záloha domu uložena.')).toBeVisible();
    };
    const p1 = await open();
    const p2 = await open();
    try {
      await setOverride(p1, a.name, '111');
      await setOverride(p2, b.name, '222');
      const after = await json<{ houseOverrides: Record<string, { waterAdvance: number }> }>(admin, 'advance-settings');
      test.info().annotations.push({ type: 'evidence', description: `přepisy po dvou souběžných úpravách: ${Object.values(after.houseOverrides).map((o) => o.waterAdvance).join(', ')}` });
      expect(after.houseOverrides[a.id]?.waterAdvance, 'přepis prvního správce').toBe(111);
      expect(after.houseOverrides[b.id]?.waterAdvance).toBe(222);
    } finally {
      await admin.put('advance-settings', { data: { houseOverrides: original } });
    }
  });
});

test.describe.serial('import počátečních dat (T13) — jen nanečisto', () => {
  const dir = (name: 'samples' | 'demo') => resolve(root, 'seed', name);
  const files = (name: 'samples' | 'demo') => readdirSync(dir(name)).filter((f) => f.endsWith('.csv')).map((f) => resolve(dir(name), f));

  async function openSeed(page: Page) {
    await go(page, '/admin/seed-import');
    await expect(page.getByRole('heading', { name: 'Import počátečních dat' })).toBeVisible();
  }

  test('ukázková data (S2/S3): zkouška nanečisto, report, stažení MD a XLSX; nic se nezapíše', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const housesBefore = (await activeHouses(admin)).length;
    await loginAs(page, users.admin);
    await openSeed(page);
    const input = page.getByLabel('Soubory CSV podle šablon');
    await input.setInputFiles(files('samples'));
    await expect(page.getByRole('list', { name: 'Nahrané soubory' }).getByRole('listitem')).toHaveCount(10);
    await page.getByRole('button', { name: 'Zkontrolovat nanečisto' }).click();
    const report = page.getByRole('region', { name: 'Report importu' });
    await expect(report).toBeVisible({ timeout: 60_000 });
    await expect(report.getByRole('table', { name: 'Soubory' }).locator('tbody tr')).toHaveCount(10);
    await expect(report.getByRole('table', { name: 'Salda domů po importu' })).toBeVisible();
    const status = await report.locator('p.rounded-lg').first().innerText();
    test.info().annotations.push({ type: 'evidence', description: `stav zkoušky: ${status}` });
    const md = await download(page, () => report.getByRole('button', { name: 'Markdown' }).click(), /md/);
    expect(md.body.toString('utf-8')).toMatch(/#/);
    const xl = await download(page, () => report.getByRole('button', { name: 'Stáhnout report (XLSX)' }).click(), /xlsx/);
    expect(isXlsx(xl.body)).toBe(true);
    test.info().annotations.push({ type: 'evidence', description: `názvy reportů: ${md.name}, ${xl.name}` });
    expect((await activeHouses(admin)).length).toBe(housesBefore);
  });

  test('konflikt: dům se stejným názvem a jinou adresou → „konflikt“, Zapsat nejde; odebrání souboru zruší report', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const existing = (await activeHouses(admin))[0];
    await loginAs(page, users.admin);
    await openSeed(page);
    const csv = `name;address;contact_person;email;active\n${existing.name};Jiná adresa ${TAG};Někdo;nekdo@example.invalid;ano\n`;
    await page.getByLabel('Soubory CSV podle šablon').setInputFiles({ name: 'houses.csv', mimeType: 'text/csv', buffer: Buffer.from(csv, 'utf-8') });
    await page.getByRole('button', { name: 'Zkontrolovat nanečisto' }).click();
    const report = page.getByRole('region', { name: 'Report importu' });
    const issues = report.getByRole('table', { name: 'Chyby a konflikty' });
    await expect(issues.getByRole('row').filter({ hasText: 'konflikt' }).first()).toBeVisible({ timeout: 60_000 });
    await expect(report.getByRole('button', { name: 'Zapsat' })).toBeDisabled();
    await expect(report).toContainText('Nelze zapsat');
    await page.getByRole('button', { name: 'Odebrat houses.csv' }).click();
    await expect(report).toHaveCount(0);
  });

  test('chybný soubor (špatná hlavička, neplatné číslo) → chyby s číslem řádku, žádný pád', async ({ page }) => {
    await loginAs(page, users.admin);
    await openSeed(page);
    await page.getByLabel('Soubory CSV podle šablon').setInputFiles([
      { name: 'components.csv', mimeType: 'text/csv', buffer: Buffer.from('kod;nazev\nX;Y\n') },
      { name: 'opening_fund_shares.csv', mimeType: 'text/csv', buffer: Buffer.from('house;date;value;source;note\nNeexistuje;2023-11-01;abc;x;\n') },
    ]);
    await page.getByRole('button', { name: 'Zkontrolovat nanečisto' }).click();
    const report = page.getByRole('region', { name: 'Report importu' });
    const alert = page.getByRole('alert');
    await expect(report.or(alert).first()).toBeVisible({ timeout: 60_000 });
    if (await report.count()) {
      const issues = report.getByRole('table', { name: 'Chyby a konflikty' });
      await expect(issues.getByRole('row').filter({ hasText: 'chyba' }).first()).toBeVisible();
      await expect(report.getByRole('button', { name: 'Zapsat' })).toBeDisabled();
    }
  });

  test('výuková data (seed/demo): zkouška nanečisto proběhne a ukáže salda', async ({ page }) => {
    await loginAs(page, users.admin);
    await openSeed(page);
    await page.getByLabel('Soubory CSV podle šablon').setInputFiles(files('demo'));
    await page.getByRole('button', { name: 'Zkontrolovat nanečisto' }).click();
    const report = page.getByRole('region', { name: 'Report importu' });
    await expect(report).toBeVisible({ timeout: 60_000 });
    await expect(report.getByRole('table', { name: 'Salda domů po importu' }).locator('tbody tr').first()).toBeVisible();
    test.info().annotations.push({ type: 'evidence', description: `demo: ${await report.locator('p.rounded-lg').first().innerText()}` });
  });
});
