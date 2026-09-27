import { api, expect, go, kc, loginAs, test, users } from './live';
import { activeHouses, download, isCzechCsv, isXlsx, json, round2 } from './data';

/**
 * T07 Saldo domu: přehled (kontrolní řádek Σ domů = rozpočteno), detail domu s průběžným saldem a „Jak vznikl“,
 * exporty XLSX/CSV, člen vidí svůj dům a kartu salda na přehledu, filtr období.
 */
interface Overview {
  from: string; to: string;
  components: { componentId: string; componentName: string; allocatedTotal: number; housesTotal: number; matches: boolean; warnings: string[] }[];
  houses: { houseId: string; houseName: string; opening: number; payments: number; costs: Record<string, number>; saldo: number }[];
}
interface Item { date: string; kind: string; description: string; amount: number; balance: number; detail: { explanation: string } | null }
interface Ledger { houseId: string; houseName: string; from: string; to: string; opening: number; payments: number; costs: number; saldo: number; items: Item[]; ownershipPeriods: { id: string }[] }

test.describe.serial('saldo domu (T07)', () => {
  test('přehled: kontrolní řádek každé složky sedí, saldo = podíl + platby − náklady, bez osobních údajů', async ({ playwright }) => {
    const admin = await api(playwright, users.admin);
    const o = await json<Overview>(admin, 'ledger/overview');
    expect(o.components.length).toBeGreaterThanOrEqual(4);
    const bad = o.components.filter((c) => !c.matches).map((c) => `${c.componentName}: ${c.housesTotal} ≠ ${c.allocatedTotal} ${c.warnings.join('; ')}`);
    expect(bad, bad.join('\n')).toEqual([]);
    for (const c of o.components) expect(round2(o.houses.reduce((s, h) => s + (h.costs[c.componentId] ?? 0), 0))).toBeCloseTo(c.housesTotal, 2);
    for (const h of o.houses) {
      const costs = Object.values(h.costs).reduce((s, v) => s + v, 0);
      expect(round2(h.opening + h.payments - costs), h.houseId).toBeCloseTo(h.saldo, 2);
    }
    const houses = await activeHouses(admin);
    expect(JSON.stringify(o)).not.toContain(houses[0].contactPerson || '@@@');
    expect(JSON.stringify(o)).not.toMatch(/@example|email/i);
  });

  test('detail domu: průběžné saldo řádek po řádku, „Jak vznikl“, souhrn = API', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const houseId = users.member.houseId!;
    const l = await json<Ledger>(admin, `ledger/houses/${houseId}`);
    let running = 0;
    const wrong: string[] = [];
    for (const i of l.items) { running = round2(running + i.amount); if (Math.abs(running - i.balance) > 0.005) wrong.push(`${i.date} ${i.description}: ${i.balance} ≠ ${running}`); }
    expect(wrong, wrong.join('\n')).toEqual([]);
    expect(round2(running)).toBeCloseTo(l.saldo, 2);
    expect(l.opening).toBe(1234.5); // fund share from spec 11
    expect(l.items.some((i) => i.kind === 'Credit')).toBe(true); // S2 credit −20 000 / 4
    expect(l.items.some((i) => i.kind === 'Water')).toBe(true);

    await loginAs(page, users.admin);
    await go(page, '/saldo-domu');
    const overview = page.getByRole('table', { name: 'Saldo domů' });
    await expect(overview).toBeVisible({ timeout: 30_000 });
    await expect(overview.locator('tfoot td').filter({ hasText: '✗' })).toHaveCount(0);
    await overview.getByRole('row').filter({ hasText: l.houseName }).click();
    const detail = page.getByRole('region', { name: `Saldo ${l.houseName}` });
    await expect(detail).toContainText(kc(l.opening));
    await expect(detail).toContainText(kc(l.payments));
    await expect(detail.getByRole('definition').last()).toContainText(l.saldo === 0 ? 'vyrovnáno' : `${l.saldo > 0 ? 'přeplatek' : 'nedoplatek'}`);
    const rows = detail.getByRole('table', { name: 'Položky salda' }).locator('tbody tr');
    await expect(rows).toHaveCount(l.items.length);
    await expect(rows.last()).toContainText(kc(l.items.at(-1)!.balance));
    const withDetail = rows.filter({ has: page.getByText('Jak vznikl') }).first();
    await withDetail.getByText('Jak vznikl').click();
    await expect(withDetail.locator('details p')).not.toBeEmpty();
    const creditRow = rows.filter({ hasText: 'Kredit' }).first();
    await expect(creditRow).toContainText(kc(5000));
  });

  test('exporty přehledu a domu (XLSX + CSV pro český Excel)', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const name = (await activeHouses(admin)).find((h) => h.id === users.member.houseId)!.name;
    await loginAs(page, users.admin);
    await go(page, '/saldo-domu');
    const section = page.getByRole('region', { name: 'Přehled domů' });
    await expect(section).toBeVisible({ timeout: 30_000 });
    const x = await download(page, () => section.getByRole('button', { name: 'Export XLSX' }).click(), /xlsx/);
    expect(isXlsx(x.body)).toBe(true);
    expect(x.name).toMatch(/\.xlsx$/);
    const c = await download(page, () => section.getByRole('button', { name: 'Export CSV' }).click(), /csv/);
    expect(isCzechCsv(c.body)).toBe(true);
    expect(c.body.toString('utf-8')).toMatch(/Σ domů/);
    expect(c.body.toString('utf-8')).toMatch(/Rozpočteno složkou/);
    await page.getByRole('table', { name: 'Saldo domů' }).getByRole('row').filter({ hasText: name }).click();
    const detail = page.getByRole('region', { name: `Saldo ${name}` });
    const hx = await download(page, () => detail.getByRole('button', { name: 'Export XLSX' }).click(), /xlsx/);
    expect(isXlsx(hx.body)).toBe(true);
    const hc = await download(page, () => detail.getByRole('button', { name: 'Export CSV' }).click(), /csv/);
    expect(isCzechCsv(hc.body)).toBe(true);
    test.info().annotations.push({ type: 'evidence', description: `soubory: ${x.name}, ${c.name}, ${hx.name}, ${hc.name}` });
  });

  test('filtr období přepočítá přehled (rok 2024)', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const o = await json<Overview>(admin, 'ledger/overview?from=2024-01-01&to=2024-12-31');
    await loginAs(page, users.admin);
    await go(page, '/saldo-domu');
    await page.getByLabel('Od (prázdné = start účtování)').fill('2024-01-01');
    await page.getByLabel('Do (prázdné = dnes)').fill('2024-12-31');
    await page.getByRole('button', { name: 'Zobrazit' }).click();
    await expect(page.getByRole('heading', { name: 'Všechny domy 1. 1. 2024 – 31. 12. 2024' })).toBeVisible();
    const first = o.houses[0];
    await expect(page.getByRole('table', { name: 'Saldo domů' }).getByRole('row').filter({ hasText: first.houseName })).toContainText(kc(first.payments));
  });

  test('člen: vidí detail jen svého domu, ostatní jen v souhrnu; karta salda na přehledu = saldo z ledgeru', async ({ page, playwright }) => {
    const member = await api(playwright, users.member);
    const l = await json<Ledger>(member, `ledger/houses/${users.member.houseId}`);
    await loginAs(page, users.member);
    const card = page.getByRole('link').filter({ hasText: 'Saldo domu' }).filter({ hasText: 'Kč' });
    const whole = Math.round(Math.abs(l.saldo)).toLocaleString('cs-CZ').replace(/\s/g, '\\s?');
    await expect(card).toContainText(new RegExp(`${whole}\\s?Kč`));
    await expect(card).toContainText(Math.round(l.saldo) === 0 ? 'Vyrovnáno' : l.saldo > 0 ? 'Přeplatek' : 'Nedoplatek');
    await card.click();
    await expect(page.getByRole('heading', { name: 'Saldo domu', level: 1 })).toBeVisible();
    await expect(page.getByRole('region', { name: `Saldo ${l.houseName}` })).toBeVisible();
    const overview = page.getByRole('table', { name: 'Saldo domů' });
    const other = overview.getByRole('row').filter({ hasNotText: l.houseName }).nth(1);
    await other.click();
    await expect(page.getByRole('region', { name: /^Saldo / })).toHaveCount(1); // still only the own house
  });

  test('účetní vidí detail libovolného domu', async ({ page, playwright }) => {
    const acc = await api(playwright, users.accountant);
    const o = await json<Overview>(acc, 'ledger/overview');
    const target = o.houses.find((h) => h.houseId !== users.member.houseId)!;
    await loginAs(page, users.accountant);
    await go(page, '/saldo-domu');
    await page.getByRole('table', { name: 'Saldo domů' }).getByRole('row').filter({ hasText: target.houseName }).click();
    await expect(page.getByRole('region', { name: `Saldo ${target.houseName}` })).toBeVisible();
  });
});
