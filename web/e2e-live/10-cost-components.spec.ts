import type { Page } from '@playwright/test';
import { RUN, api, expect, go, loginAs, test, users } from './live';
import { START, componentByCode, componentDetail, roles } from './data';

/**
 * T02 Nákladové složky — the accounting set-up everything else builds on:
 * VODA_PVK (podle odečtů, spotřeba), ZTRATY_VODY (ztráty, rovným dílem → poměrem od 2026), ELEKTRINA_VODARNA (4 domy),
 * OSVETLENI (všechny domy, jeden se připojí později). Idempotent: existing components are reused.
 */
const canonical = [
  { code: 'VODA_PVK', name: 'Voda PVK', basis: 'Metered', role: 'Consumption' },
  { code: 'ZTRATY_VODY', name: 'Ztráty vody', basis: 'Metered', role: 'Losses' },
  { code: 'ELEKTRINA_VODARNA', name: 'Elektřina – vodárna', basis: 'CostEntries', role: 'None' },
  { code: 'OSVETLENI', name: 'Osvětlení', basis: 'CostEntries', role: 'None' },
] as const;

const LATE_JOIN = '2025-10-01';
const LOSS_RATIO_FROM = '2026-01-01';

async function openComponents(page: Page) {
  await go(page, '/admin/cost-components');
  await expect(page.getByRole('heading', { name: 'Nákladové složky' })).toBeVisible();
  await expect(page.locator('.animate-spin')).toHaveCount(0, { timeout: 30_000 });
}

async function createViaForm(page: Page, c: { code: string; name: string; basis: string; role: string }, start = START) {
  await page.getByRole('button', { name: 'Nová složka' }).click();
  const form = page.locator('form').filter({ has: page.getByRole('button', { name: 'Založit' }) });
  await form.getByLabel('Název').fill(c.name);
  await form.getByLabel('Kód').fill(c.code.toLowerCase()); // the field upper-cases
  await expect(form.getByLabel('Kód')).toHaveValue(c.code);
  await form.getByLabel('Účtuje se od').fill(start);
  await form.getByLabel('Náklady').selectOption(c.basis);
  if (c.basis === 'Metered') await form.getByLabel('Role ve vyúčtování vody').selectOption(c.role);
  await form.getByLabel('Poznámka').fill(`${RUN} živý test`);
  await form.getByRole('button', { name: 'Založit' }).click();
  return form;
}

test.describe.serial('nákladové složky (T02)', () => {
  test('správce založí čtyři složky přes formulář (nebo je najde z minulého běhu)', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    await loginAs(page, users.admin);
    await openComponents(page);
    for (const c of canonical) {
      if (await componentByCode(admin, c.code)) continue;
      await createViaForm(page, c);
      await expect(page.getByRole('region', { name: `Detail složky ${c.name}` })).toBeVisible();
    }
    const table = page.getByRole('table').first();
    for (const c of canonical) {
      const row = table.getByRole('row').filter({ hasText: c.code });
      await expect(row).toHaveCount(1);
      await expect(row).toContainText(c.basis === 'Metered' ? 'Z odečtů vodoměrů' : 'Z nákladových záznamů');
    }
    await expect(table.getByRole('row').filter({ hasText: 'VODA_PVK' })).toContainText('Voda PVK (spotřeba domů)');
    await expect(table.getByRole('row').filter({ hasText: 'ZTRATY_VODY' })).toContainText('Ztráty vody');
    const created = await componentByCode(admin, 'ZTRATY_VODY');
    expect(created?.waterRole).toBe('Losses');
    expect((await componentDetail(admin, created!.id)).rules[0]).toMatchObject({ validFrom: START, method: 'Equal' });
  });

  test('validace založení: duplicitní kód, druhá role „spotřeba“, prázdný start', async ({ page, playwright }) => {
    await loginAs(page, users.admin);
    await openComponents(page);
    // duplicate code → 409 with a message, nothing new in the list
    const form = await createViaForm(page, { code: 'VODA_PVK', name: `Duplicitní ${RUN}`, basis: 'CostEntries', role: 'None' });
    await expect(form.getByRole('alert')).toContainText(/existuje|už|kód/i);
    // a second Consumption component is refused (each water role at most once)
    await form.getByLabel('Kód').fill('E2E_DRUHA_VODA');
    await form.getByLabel('Náklady').selectOption('Metered');
    await form.getByLabel('Role ve vyúčtování vody').selectOption('Consumption');
    await form.getByRole('button', { name: 'Založit' }).click();
    await expect(form.getByRole('alert')).toContainText(/spotřeb|role|jen jedn|už/i);
    // invalid code characters
    await form.getByLabel('Kód').fill('ŠPATNÝ KÓD');
    await form.getByLabel('Náklady').selectOption('CostEntries');
    await form.getByRole('button', { name: 'Založit' }).click();
    await expect(form.getByRole('alert')).toBeVisible();
    const admin = await api(playwright, users.admin);
    const all = await (await admin.get('cost-components')).json();
    expect(all.filter((c: { name: string }) => c.name.includes(RUN))).toEqual([]);
  });

  test('účast domů: Elektřina – vodárna pro 4 domy přes formulář, překryv odmítnut, omyl smazán', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const { vodarna, lateJoiner } = await roles(admin, users.member.houseId);
    const comp = (await componentByCode(admin, 'ELEKTRINA_VODARNA'))!;
    // leftover of an interrupted earlier run: the participation "added by mistake"
    for (const p of (await componentDetail(admin, comp.id)).participations.filter((x) => x.houseId === lateJoiner.id && x.validFrom === '2026-09-01'))
      await admin.delete(`cost-components/${comp.id}/participations/${p.id}?reason=cleanup`);
    await loginAs(page, users.admin);
    await openComponents(page);
    await page.getByRole('row').filter({ hasText: 'ELEKTRINA_VODARNA' }).click();
    const detail = page.getByRole('region', { name: `Detail složky ${comp.name}` });
    const form = detail.getByRole('form', { name: 'Přidat účast' });
    const existing = (await componentDetail(admin, comp.id)).participations;
    for (const h of vodarna) {
      if (existing.some((p) => p.houseId === h.id)) continue;
      await form.getByLabel('Dům').selectOption({ label: h.name });
      await form.getByLabel('Od').fill(START);
      await form.getByRole('button', { name: 'Přidat účast' }).click();
      await expect(detail.getByRole('listitem').filter({ hasText: h.name }).first()).toBeVisible();
    }
    await expect.poll(async () => (await componentDetail(admin, comp.id)).participations.filter((p) => !p.validTo).length).toBe(4);

    // overlap: the same house again from a later date → refused with a reason
    await form.getByLabel('Dům').selectOption({ label: vodarna[0].name });
    await form.getByLabel('Od').fill('2024-05-01');
    await form.getByRole('button', { name: 'Přidat účast' }).click();
    await expect(detail.getByRole('alert')).toContainText(/překr|už|současně|účast/i);

    // before the component start → refused
    await form.getByLabel('Dům').selectOption({ label: lateJoiner.name });
    await form.getByLabel('Od').fill('2023-10-01');
    await form.getByRole('button', { name: 'Přidat účast' }).click();
    await expect(detail.getByRole('alert')).toContainText(/start|před|od /i);

    // added by mistake → Smazat asks for confirmation (#19)
    await form.getByLabel('Od').fill('2026-09-01');
    await form.getByRole('button', { name: 'Přidat účast' }).click();
    const wrong = detail.getByRole('listitem').filter({ hasText: lateJoiner.name }).filter({ hasText: '1. 9. 2026' });
    await expect(wrong).toBeVisible();
    await wrong.getByRole('button', { name: 'Smazat' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toContainText('Smazat účast?');
    await dialog.getByLabel(/Důvod/).fill(`${RUN} omyl`);
    await dialog.getByRole('button', { name: 'Smazat účast' }).click();
    await expect(wrong).toHaveCount(0);
    expect((await componentDetail(admin, comp.id)).participations.map((p) => p.houseId).sort()).toEqual(vodarna.map((h) => h.id).sort());
  });

  test('účast ostatních složek: voda a ztráty všechny domy, osvětlení s pozdějším připojením (API set-up) + ukončení k datu v UI', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const { houses, lateJoiner } = await roles(admin, users.member.houseId);
    for (const code of ['VODA_PVK', 'ZTRATY_VODY', 'OSVETLENI']) {
      const comp = (await componentByCode(admin, code))!;
      const have = (await componentDetail(admin, comp.id)).participations;
      for (const h of houses) {
        if (have.some((p) => p.houseId === h.id)) continue;
        const from = code === 'OSVETLENI' && h.id === lateJoiner.id ? LATE_JOIN : START;
        const res = await admin.post(`cost-components/${comp.id}/participations`, { data: { houseId: h.id, validFrom: from, reason: `${RUN} set-up` } });
        expect(res.status(), await res.text()).toBe(201);
      }
    }
    // UI: segments of OSVETLENI show the change on 1. 10. 2025 (N−1 houses → N houses)
    const osv = (await componentByCode(admin, 'OSVETLENI'))!;
    await loginAs(page, users.admin);
    await openComponents(page);
    await page.getByRole('row').filter({ hasText: 'OSVETLENI' }).click();
    const detail = page.getByRole('region', { name: `Detail složky ${osv.name}` });
    await detail.getByLabel('Od', { exact: true }).last().fill('2025-01-01');
    await detail.getByLabel('Do', { exact: true }).last().fill('2026-06-30');
    await detail.getByRole('button', { name: 'Zobrazit úseky' }).click();
    const segments = detail.getByRole('table', { name: 'Úseky' });
    await expect(segments.getByRole('row')).toHaveCount(3); // header + 2 segments
    await expect(segments.getByRole('row').nth(1)).toContainText('1. 1. 2025 – 30. 9. 2025');
    await expect(segments.getByRole('row').nth(1)).not.toContainText(lateJoiner.name);
    await expect(segments.getByRole('row').nth(2)).toContainText('1. 10. 2025 – 30. 6. 2026');
    await expect(segments.getByRole('row').nth(2)).toContainText(lateJoiner.name);
    await expect(segments.getByRole('row').nth(1).getByRole('cell').nth(1)).toHaveText('273');

    // end the late joiner's participation at a future date via „Ukončit k datu“
    const row = detail.getByRole('listitem').filter({ hasText: lateJoiner.name });
    await row.getByLabel(`Poslední den účasti ${lateJoiner.name}`).fill('2027-12-31');
    await row.getByRole('button', { name: 'Ukončit k datu' }).click();
    await expect(detail.getByRole('listitem').filter({ hasText: lateJoiner.name })).toContainText('1. 10. 2025 – 31. 12. 2027');
  });

  test('změna metody ztrát od 2026 na poměr (s důvodem) + chybné změny odmítnuty', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const losses = (await componentByCode(admin, 'ZTRATY_VODY'))!;
    // re-run: remove the rule of a previous run so the UI flow is exercised again
    for (const r of (await componentDetail(admin, losses.id)).rules.filter((x) => x.validFrom === LOSS_RATIO_FROM))
      await admin.delete(`cost-components/${losses.id}/rules/${r.id}?reason=${encodeURIComponent(`${RUN} opakovaný běh`)}`);

    await loginAs(page, users.admin);
    await openComponents(page);
    await page.getByRole('row').filter({ hasText: 'ZTRATY_VODY' }).click();
    const detail = page.getByRole('region', { name: `Detail složky ${losses.name}` });
    const form = detail.getByRole('form', { name: 'Změnit metodu' });

    // before the component start → refused
    await form.getByLabel('Nová metoda od').fill('2023-01-01');
    await form.getByRole('combobox').selectOption('Equal');
    await form.getByLabel('Důvod').fill(`${RUN} chybně`);
    await form.getByRole('button', { name: 'Změnit metodu' }).click();
    await expect(detail.getByRole('alert')).toBeVisible();

    // Percent without weights = 100 % → refused
    await form.getByLabel('Nová metoda od').fill(LOSS_RATIO_FROM);
    await form.getByRole('combobox').selectOption('Percent');
    await form.getByRole('button', { name: 'Změnit metodu' }).click();
    await expect(detail.getByRole('alert')).toContainText(/100|procent/i);

    // Ratio by water consumption from 1. 1. 2026
    await form.getByRole('combobox').selectOption('Ratio');
    await form.getByLabel('Váhy podle (kód složky)').fill('voda_pvk');
    await form.getByLabel('Důvod').fill(`${RUN} hlasování schůze`);
    await form.getByRole('button', { name: 'Změnit metodu' }).click();
    await expect(detail.getByRole('listitem').filter({ hasText: 'Poměrem' })).toContainText('1. 1. 2026 – dosud');
    await expect(detail.getByRole('listitem').filter({ hasText: 'Poměrem' })).toContainText('váhy z VODA_PVK');
    await expect(detail.getByRole('listitem').filter({ hasText: 'Rovným dílem' })).toContainText('1. 11. 2023 – 31. 12. 2025');
    const rules = (await componentDetail(admin, losses.id)).rules;
    expect(rules.find((r) => r.validFrom === LOSS_RATIO_FROM)).toMatchObject({ method: 'Ratio' });
    await expect(page.getByRole('row').filter({ hasText: 'ZTRATY_VODY' })).toContainText('Poměrem');
  });

  test('účetní vidí složky přes API jen ke čtení, zápis 403', async ({ playwright }) => {
    const acc = await api(playwright, users.accountant);
    const list = await (await acc.get('cost-components')).json();
    expect(list.map((c: { code: string }) => c.code)).toEqual(expect.arrayContaining(canonical.map((c) => c.code)));
    expect((await acc.post('cost-components', { data: { name: 'x', code: 'X', startDate: START, allocationBasis: 'CostEntries' } })).status()).toBe(403);
  });
});
