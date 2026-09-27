import { API, APP, api, expect, test, token, users } from './live';

test.describe('veřejné části a zabezpečení (bez přihlášení)', () => {
  test('API hlásí prostředí dev', async ({ playwright }) => {
    const r = await (await api(playwright)).get('environment');
    expect(r.status()).toBe(200);
    expect(await r.json()).toEqual({ environment: 'dev' });
  });

  test('přihlašovací stránka se načte s pruhem prostředí', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/login/);
    await expect(page.getByText(/VÝVOJOVÉ PROSTŘEDÍ/i)).toBeVisible();
    await expect(page.getByRole('button', { name: /Microsoft/i })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Email' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Poslat přihlašovací odkaz' })).toBeVisible();
  });

  test('chráněné API bez tokenu → 401, s cizím podpisem → 401, prošlý token → 401', async ({ playwright }) => {
    for (const path of ['houses', 'ledger/overview', 'cost-components', 'cash-book', 'audit-log', 'users']) {
      const anon = await (await api(playwright)).get(path);
      expect(anon.status(), `anon ${path}`).toBe(401);
    }
    const forged = await (await api(playwright, undefined, token(users.admin, { secret: 'x'.repeat(40) }))).get('houses');
    expect(forged.status()).toBe(401);
    const expired = await (await api(playwright, undefined, token(users.admin, { expiresIn: -600 }))).get('houses');
    expect(expired.status()).toBe(401);
    const garbage = await (await api(playwright, undefined, 'not-a-jwt')).get('houses');
    expect(garbage.status()).toBe(401);
  });

  test('token neexistujícího uživatele → 403/401, ne data', async ({ playwright }) => {
    const r = await (await api(playwright, { id: 'nobody-e2e', email: 'nobody-e2e@example.invalid', role: 'Admin' })).get('houses');
    expect([401, 403]).toContain(r.status());
  });

  test('anonymní seed je na DEV vypnutý nebo neškodný', async ({ playwright }) => {
    const r = await (await api(playwright)).post('seed');
    test.info().annotations.push({ type: 'seed', description: `POST /seed → ${r.status()}` });
    expect([404, 401, 403]).toContain(r.status());
  });

  test('magic link pro neznámý e-mail odpoví stejně jako pro známý (bez prozrazení účtů)', async ({ playwright }) => {
    const ctx = await api(playwright);
    const unknown = await ctx.post('auth/magic-link', { data: { email: `neexistuje-${Date.now()}@example.invalid` } });
    expect(unknown.status()).toBe(200);
    const bad = await ctx.post('auth/magic-link/verify', { data: { token: 'nesmysl', email: users.admin.email } });
    expect([400, 401]).toContain(bad.status());
  });

  test('CORS: povolený původ aplikace, cizí původ ne', async ({ playwright }) => {
    const ctx = await api(playwright);
    const ok = await ctx.fetch('houses', { method: 'OPTIONS', headers: { Origin: APP, 'Access-Control-Request-Method': 'GET', 'Access-Control-Request-Headers': 'authorization' } });
    expect(ok.headers()['access-control-allow-origin']).toBe(APP);
    const evil = await ctx.fetch('houses', { method: 'OPTIONS', headers: { Origin: 'https://evil.example', 'Access-Control-Request-Method': 'GET' } });
    expect(evil.headers()['access-control-allow-origin'] ?? '').not.toBe('https://evil.example');
    expect(evil.headers()['access-control-allow-origin'] ?? '').not.toBe('*');
  });

  test('SPA: přímé otevření hluboké adresy vrátí aplikaci (fallback)', async ({ page }) => {
    for (const path of ['/navod', '/saldo-domu', '/admin/seed-import', '/neexistujici-stranka']) {
      const r = await page.goto(path);
      expect(r?.status(), path).toBe(200);
      await expect(page.locator('#root')).not.toBeEmpty();
    }
  });

  test('screenshoty návodu jsou dostupné jako obrázky (/navod/*.png)', async ({ request }) => {
    const r = await request.get(`${APP}/navod/odecty-1.png`);
    expect(r.status()).toBe(200);
    expect(r.headers()['content-type']).toContain('image/png');
  });

  test('bezpečnostní hlavičky frontendu', async ({ request }) => {
    const r = await request.get(`${APP}/`);
    const h = r.headers();
    test.info().annotations.push({ type: 'headers', description: JSON.stringify({ csp: h['content-security-policy'] ?? null, xfo: h['x-frame-options'] ?? null, xcto: h['x-content-type-options'] ?? null, hsts: h['strict-transport-security'] ?? null, referrer: h['referrer-policy'] ?? null }) });
    expect(h['x-content-type-options'] ?? 'MISSING', 'X-Content-Type-Options').toBe('nosniff');
  });

  test('API odpovídá rozumně rychle (studený start < 15 s, teplý < 3 s)', async ({ playwright }) => {
    const ctx = await api(playwright, users.admin);
    let t = Date.now();
    expect((await ctx.get('houses')).status()).toBe(200);
    const first = Date.now() - t;
    t = Date.now();
    expect((await ctx.get('houses')).status()).toBe(200);
    const second = Date.now() - t;
    test.info().annotations.push({ type: 'timing', description: `first ${first} ms, second ${second} ms` });
    expect(first).toBeLessThan(15_000);
    expect(second).toBeLessThan(3_000);
  });
});

void API;
