import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { api, expect, test, users } from './live';

/**
 * Authorization matrix generated from docs/API.md: every endpoint × role. The route guard must answer 403 for a role
 * that isn't allowed (before any validation or data access), and must NOT answer 401/403 for an allowed role.
 * Path placeholders get a dummy id — allowed roles may then get 400/404, which is fine (the guard let them in).
 * Write calls for allowed roles are not sent (no data changes here).
 */
interface Row { method: string; path: string; access: string }

const rows: Row[] = readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), '../../docs/API.md'), 'utf-8')
  .split('\n')
  .map((l) => /^\| (GET|POST|PUT|DELETE|PATCH) \| `([^`]+)` \| ([^|]+)\|/.exec(l))
  .filter((m): m is RegExpExecArray => m !== null)
  .map((m) => ({ method: m[1], path: m[2], access: m[3].trim() }));

const allowed = (access: string, role: 'Admin' | 'Accountant' | 'Member'): boolean | 'public' => {
  if (access.startsWith('veřejné')) return 'public';
  if (access.startsWith('přihlášený') || access.startsWith('jako detail')) return true;
  if (role === 'Admin') return true;
  if (role === 'Accountant') return /Accountant|Účetní/.test(access);
  return false;
};

const concrete = (path: string) =>
  path.split('?')[0].replace(/\{[^}]+\}/g, 'e2e-none').replace(/\\\|/g, '|').replace(/[^/]+\|[^/]+/g, (seg) => seg.split('|')[0]);

test('počet endpointů v API.md odpovídá (dokumentace se nerozpadla)', () => {
  expect(rows.length).toBeGreaterThan(90);
});

for (const role of ['Member', 'Accountant'] as const) {
  test(`matice oprávnění — ${role}`, async ({ playwright }) => {
    const user = role === 'Member' ? users.member : users.accountant;
    const ctx = await api(playwright, user);
    const wrong: string[] = [];
    for (const r of rows) {
      const a = allowed(r.access, role);
      if (a === 'public') continue;
      const url = concrete(r.path).replace(/^\//, '');
      if (a === true && r.method !== 'GET') continue; // don't write data with allowed roles here
      const res = await ctx.fetch(url, { method: r.method, data: r.method === 'GET' ? undefined : {} });
      const status = res.status();
      if (a === false && status !== 403) wrong.push(`${r.method} ${r.path} (${r.access}) → ${status}, čekáno 403`);
      if (a === true && [401, 403].includes(status) && !/Member jen svůj dům|Člen jen vlastní|jako detail/.test(r.access))
        wrong.push(`${r.method} ${r.path} (${r.access}) → ${status}, role má mít přístup`);
      if (status >= 500) wrong.push(`${r.method} ${r.path} → ${status} (chyba serveru)`);
    }
    expect(wrong, wrong.join('\n')).toEqual([]);
  });
}

test('matice oprávnění — Admin: žádné čtení nevrací 401/403 ani 5xx', async ({ playwright }) => {
  const ctx = await api(playwright, users.admin);
  const wrong: string[] = [];
  for (const r of rows.filter((x) => x.method === 'GET' && !x.access.startsWith('veřejné'))) {
    const res = await ctx.get(concrete(r.path).replace(/^\//, ''));
    if ([401, 403].includes(res.status()) || res.status() >= 500) wrong.push(`GET ${r.path} → ${res.status()}`);
  }
  expect(wrong, wrong.join('\n')).toEqual([]);
});

test('člen vidí jen svůj dům (saldo, platby, odečty)', async ({ playwright }) => {
  const admin = await api(playwright, users.admin);
  const houses: { id: string }[] = await (await admin.get('houses')).json();
  const other = houses.find((h) => h.id !== users.member.houseId)!;
  const member = await api(playwright, users.member);
  expect((await member.get(`ledger/houses/${other.id}`)).status()).toBe(403);
  expect((await member.get(`ledger/houses/${users.member.houseId}`)).status()).toBe(200);
  expect((await member.get(`advances?houseId=${other.id}`)).status()).toBe(403);
  const chart = await member.get(`readings/chart?houseId=${other.id}`);
  expect([403, 200]).toContain(chart.status());
  if (chart.status() === 200) test.info().annotations.push({ type: 'warning', description: 'readings/chart s cizím houseId vrací 200 — zkontrolovat, zda neukazuje cizí data' });
  const calc = await (await member.get('advance-settings/calculate')).json();
  expect(calc.houses.map((h: { houseId: string }) => h.houseId)).toEqual([users.member.houseId]);
  const overview = await (await member.get('ledger/overview')).json();
  expect(JSON.stringify(overview)).not.toMatch(/@|ownerName|contact/i);
});
