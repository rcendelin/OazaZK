import type { Page } from '@playwright/test';
import { RUN, api, expect, go, loginAs, test, users } from './live';
import { START, activeHouses, componentByCode, json, meters, roles } from './data';
import type { Reading } from './data';

/**
 * T03 Počáteční stavy: start účtování k 1. 11. 2023, stavy vodoměrů (návrh z odečtů = T04 interpolace),
 * podíly ve fondu (+/−), kredit složky u dodavatele (S2: −20 000 → 4 × −5 000), úprava, smazání a validace.
 */
interface Balance { key: string; type: string; houseId: string | null; meterId: string | null; componentId: string | null; date: string; value: number; isEstimate: boolean; source: string }

async function openPage(page: Page) {
  await go(page, '/admin/opening-balances');
  await expect(page.getByRole('heading', { name: 'Počáteční stavy', exact: true })).toBeVisible();
  await expect(page.locator('.animate-spin')).toHaveCount(0, { timeout: 30_000 });
}

const dayNumber = (iso: string) => Date.parse(`${iso.slice(0, 10)}T00:00:00Z`) / 86_400_000;

test.describe.serial('počáteční stavy (T03)', () => {
  test('start účtování k 1. 11. 2023 založí období vlastnictví všem aktivním domům (idempotentně)', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    await loginAs(page, users.admin);
    await openPage(page);
    const section = page.getByRole('region', { name: 'Start účtování' });
    await section.getByLabel('Datum startu účtování').fill(START);
    await section.getByRole('button', { name: 'Start účtování k datu' }).click();
    await expect(section.getByRole('status')).toHaveText(/Založeno \d+ období vlastnictví od 1\. 11\. 2023\.|Všechny domy už období vlastnictví mají/);
    await expect(section.getByText(/^Bez období vlastnictví:/)).toHaveCount(0);
    const periods = await json<{ houseId: string; validFrom: string }[]>(admin, 'ownership-periods');
    for (const h of await activeHouses(admin)) expect(periods.some((p) => p.houseId === h.id), `období pro dům ${h.id}`).toBe(true);
    // second click changes nothing
    await section.getByRole('button', { name: 'Start účtování k datu' }).click();
    await expect(section.getByRole('status')).toHaveText('Všechny domy už období vlastnictví mají, nic se nezměnilo.');
  });

  test('stavy vodoměrů: „Navrhnout z odečtů“ = interpolace z odečtů (T04), uloží se jako odhad a zapíše odečet', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const houses = await activeHouses(admin);
    const allMeters = await meters(admin);
    const readings = await json<Reading[]>(admin, 'readings/all');
    await loginAs(page, users.admin);
    await openPage(page);
    const section = page.getByRole('region', { name: 'Stavy vodoměrů' });

    for (const m of allMeters) {
      const label = m.type === 'Main' ? `Hlavní vodoměr ${m.meterNumber}` : houses.find((h) => h.id === m.houseId)?.name;
      if (!label) continue; // meter of an inactive house
      const value = section.getByLabel(`Hodnota ${label}`, { exact: true });
      const row = section.locator('div.space-y-2').filter({ has: page.getByLabel(`Hodnota ${label}`, { exact: true }) });
      // the row proposes for the day of the current ownership period (after a house transfer that is the transfer day, #9)
      const asked = page.waitForRequest((r) => r.url().includes('/readings/estimate') && r.url().includes(m.id));
      await row.getByRole('button', { name: 'Navrhnout z odečtů' }).click();
      const day = new URL((await asked).url()).searchParams.get('date')!;
      await expect(row.getByRole('status')).toBeVisible();
      const estimate = await json<{ value: number | null; isEstimate: boolean; note: string; method: string }>(admin, `readings/estimate?meterId=${m.id}&date=${day}`);
      if (estimate.value === null) { await expect(row.getByRole('status')).toContainText(/odečt/); continue; }
      await expect(value).toHaveValue(String(estimate.value).replace('.', ','));
      await expect(row.getByLabel(`Zdroj ${label}`)).toHaveValue(estimate.note);

      // cross-check the interpolation against the neighbouring physical readings (S8 formula)
      const own = readings.filter((r) => r.meterId === m.id && r.readingDate.slice(0, 10) !== day)
        .sort((a, b) => a.readingDate.localeCompare(b.readingDate));
      const before = own.filter((r) => r.readingDate.slice(0, 10) < day).at(-1);
      const after = own.find((r) => r.readingDate.slice(0, 10) > day);
      if (before && after && estimate.method === 'Interpolated') {
        const expected = before.value + (after.value - before.value) * (dayNumber(day) - dayNumber(before.readingDate)) / (dayNumber(after.readingDate) - dayNumber(before.readingDate));
        expect(estimate.value).toBeCloseTo(expected, 3);
        expect(estimate.isEstimate).toBe(true);
      }
      await row.getByRole('button', { name: /^Uložit/ }).click();
      await expect(row.getByRole('status')).toHaveText('Uloženo.');
    }
    const balances = await json<Balance[]>(admin, 'opening-balances');
    // (a house transfer adds a second meter balance for the new owner at the transfer date)
    const meterBalances = balances.filter((b) => b.type === 'MeterReading');
    const shown = allMeters.filter((m) => m.type === 'Main' || houses.some((h) => h.id === m.houseId));
    expect(shown.every((m) => meterBalances.some((b) => b.meterId === m.id))).toBe(true);
    expect(meterBalances.every((b) => b.source.length > 0)).toBe(true);
    // the opening value was written as a reading on the start day too
    const after = await json<Reading[]>(admin, 'readings/all');
    for (const b of meterBalances) expect(after.some((r) => r.meterId === b.meterId && r.readingDate.startsWith(START)), `odečet k ${START} pro ${b.meterId}`).toBe(true);
  });

  test('podíl ve fondu: přeplatek i nedoplatek, český formát čísla, úprava a smazání', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const { others } = await roles(admin, users.member.houseId);
    const member = (await activeHouses(admin)).find((h) => h.id === users.member.houseId)!;
    const debtor = others[0];
    const scratch = others[1];
    await loginAs(page, users.admin);
    await openPage(page);
    const section = page.getByRole('region', { name: 'Podíl ve fondu spolku' });
    const row = (name: string) => section.locator('div.space-y-2').filter({ has: page.getByLabel(`Hodnota ${name}`, { exact: true }) });

    const save = async (name: string, value: string, source: string) => {
      const r = row(name);
      await r.getByLabel(`Hodnota ${name}`, { exact: true }).fill(value);
      await r.getByLabel(`Zdroj ${name}`).fill(source);
      await r.getByRole('button', { name: /^Uložit/ }).click();
      await expect(r.getByRole('status')).toHaveText('Uloženo.');
      // after the refetch a new row re-mounts as an existing balance (the „Uloženo.“ message disappears with it)
      await expect(r.getByRole('button', { name: 'Uložit změnu' })).toBeVisible();
      await expect(r.getByLabel(`Zdroj ${name}`)).toHaveValue(source);
    };
    await save(member.name, '1 234,50', `závěrka 2022/23 ${RUN}`);
    await save(debtor.name, '-800', `závěrka 2022/23 ${RUN}`);
    await save(scratch.name, '100', `omyl ${RUN}`);

    let balances = await json<Balance[]>(admin, 'opening-balances');
    const fund = (id: string) => balances.find((b) => b.type === 'FundShare' && b.houseId === id);
    expect(fund(member.id)?.value).toBe(1234.5);
    expect(fund(debtor.id)?.value).toBe(-800);
    expect(fund(scratch.id)?.value).toBe(100);

    // Save without a source is not possible (button disabled)
    await row(scratch.name).getByLabel(`Zdroj ${scratch.name}`).fill('');
    await expect(row(scratch.name).getByRole('button', { name: 'Uložit změnu' })).toBeDisabled();
    // deleted by mistake → back to a fresh row
    await row(scratch.name).getByRole('button', { name: 'Smazat' }).click();
    await expect(row(scratch.name).getByRole('button', { name: 'Uložit', exact: true })).toBeVisible();
    balances = await json<Balance[]>(admin, 'opening-balances');
    expect(fund(scratch.id)).toBeUndefined();

    // a duplicate via the API → 409
    const dup = await admin.post('opening-balances', { data: { type: 'FundShare', houseId: member.id, date: START, value: 1, isEstimate: false, source: 'dup' } });
    expect(dup.status()).toBe(409);
  });

  test('kredit složky u dodavatele: náhled −20 000 → 4 × −5 000 (S2) a uložení', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const comp = (await componentByCode(admin, 'ELEKTRINA_VODARNA'))!;
    await loginAs(page, users.admin);
    await openPage(page);
    const section = page.getByRole('region', { name: 'Kredit složky u dodavatele' });
    const value = section.getByLabel(`Hodnota ${comp.name}`, { exact: true });
    const row = section.locator('div.space-y-2').filter({ has: page.getByLabel(`Hodnota ${comp.name}`, { exact: true }) });
    await value.fill('-20 000');
    await row.getByRole('button', { name: 'Náhled rozdělení' }).click();
    const split = row.getByRole('table', { name: `Rozdělení ${comp.name}` });
    await expect(split.getByRole('row')).toHaveCount(4);
    for (const r of await split.getByRole('row').all()) await expect(r).toContainText(/[-−]5\s?000\s?Kč/);
    await row.getByLabel(`Zdroj ${comp.name}`).fill(`smlouva s dodavatelem ${RUN}`);
    await row.getByRole('button', { name: /^Uložit/ }).click();
    await expect(row.getByRole('status')).toHaveText('Uloženo.');
    const balances = await json<Balance[]>(admin, 'opening-balances');
    expect(balances.find((b) => b.type === 'ComponentCredit' && b.componentId === comp.id)?.value).toBe(-20000);
    const preview = await json<{ shares: { amount: number }[] }>(admin, `opening-balances/component-credit-preview?componentId=${comp.id}&date=${START}&value=-20000`);
    expect(preview.shares.map((s) => s.amount)).toEqual([-5000, -5000, -5000, -5000]);
  });

  test('zadání s typografickým mínusem (−20 000, jak radí nápověda) nesmí tiše uložit 0', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const comp = (await componentByCode(admin, 'ELEKTRINA_VODARNA'))!;
    await loginAs(page, users.admin);
    await openPage(page);
    const section = page.getByRole('region', { name: 'Kredit složky u dodavatele' });
    const value = section.getByLabel(`Hodnota ${comp.name}`, { exact: true });
    const row = section.locator('div.space-y-2').filter({ has: page.getByLabel(`Hodnota ${comp.name}`, { exact: true }) });
    await value.fill('−20 000');
    await row.getByRole('button', { name: 'Náhled rozdělení' }).click();
    const split = row.getByRole('table', { name: `Rozdělení ${comp.name}` });
    await expect(split.getByRole('row')).toHaveCount(4);
    test.info().annotations.push({ type: 'evidence', description: `náhled: ${await split.innerText()}` });
    await expect(split.getByRole('row').first()).toContainText(/[-−]5\s?000/, { timeout: 5_000 });
  });

  test('nečíselná hodnota („abc“) se neuloží jako 0', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const { others } = await roles(admin, users.member.houseId);
    const target = others[2];
    await loginAs(page, users.admin);
    await openPage(page);
    const section = page.getByRole('region', { name: 'Podíl ve fondu spolku' });
    const value = section.getByLabel(`Hodnota ${target.name}`, { exact: true });
    const row = section.locator('div.space-y-2').filter({ has: page.getByLabel(`Hodnota ${target.name}`, { exact: true }) });
    await value.fill('abc');
    await row.getByLabel(`Zdroj ${target.name}`).fill(`${RUN} neplatné číslo`);
    await row.getByRole('button', { name: /^Uložit/ }).click();
    try {
      await expect(row.getByRole('alert')).toBeVisible({ timeout: 10_000 });
    } finally {
      // clean up whatever got saved
      const balances = await json<Balance[]>(admin, 'opening-balances');
      const saved = balances.find((b) => b.type === 'FundShare' && b.houseId === target.id);
      test.info().annotations.push({ type: 'evidence', description: `uloženo: ${JSON.stringify(saved?.value)}` });
      if (saved) await admin.delete(`opening-balances/${encodeURIComponent(saved.key)}?reason=cleanup`);
    }
  });

  test('účetní vidí počáteční stavy přes API, zápis 403', async ({ playwright }) => {
    const acc = await api(playwright, users.accountant);
    expect((await acc.get('opening-balances')).status()).toBe(200);
    expect((await acc.get('ownership-periods')).status()).toBe(200);
    expect((await acc.post('opening-balances', { data: {} })).status()).toBe(403);
    expect((await acc.post('ownership-periods/start', { data: { startDate: START } })).status()).toBe(403);
  });
});
