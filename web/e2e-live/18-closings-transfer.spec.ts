import type { APIRequestContext, Page } from '@playwright/test';
import { APP, RUN, api, expect, go, loginAs, test, users } from './live';
import { TODAY, activeHouses, componentByCode, download, isCzechCsv, isXlsx, json, lastClosedDay, meters, roles, shift } from './data';
import type { Reading } from './data';

/**
 * T08 Mezizávěrky: uzávěrka všech domů, co se po ní dá a nedá měnit (náklad → oprava s důvodem, platba → 409 / zaúčtování
 * po řezu, odečet → 409, pravidla/účast/počáteční stav/pokladna), export pro účetní, zrušení poslední s důvodem.
 * T03 Převod domu: náhled, převod (mezizávěrka domu k D−1), dvě období vlastnictví v saldu; nakonec se mezizávěrka převodu
 * zruší, aby šel běh zopakovat (převod domu sám zůstane).
 */
const TAG = `${RUN}-${Date.now().toString(36).slice(-4)}`;
interface Closing { id: string; date: string; scope: 'All' | 'House'; houseId: string | null; houseName: string | null; reason: string; createdByName: string | null; canDelete: boolean; houses?: { houseId: string; saldo: number; currentSaldo: number; difference: number }[] }
interface Payment { houseId: string; rowKey: string; type: string; paymentDate: string; note: string | null; amount: number }

const czDay = (iso: string) => { const [y, m, d] = iso.split('-').map(Number); return `${d}. ${m}. ${y}`; };
const closings = (ctx: APIRequestContext) => json<Closing[]>(ctx, 'interim-closings');
const enc = encodeURIComponent;

let C = '2025-12-31';

async function openClosings(page: Page) {
  await go(page, '/mezizaverky');
  await expect(page.getByRole('heading', { name: 'Mezizávěrky' })).toBeVisible();
  await expect(page.locator('.animate-spin')).toHaveCount(0, { timeout: 30_000 });
}

test.describe.serial('mezizávěrky (T08)', () => {
  test('mezizávěrka všech domů přes formulář; dnešní a dřívější datum odmítnuto', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const last = await lastClosedDay(admin);
    if (last && last >= C) C = shift(last, 1);
    // leftovers of interrupted earlier runs (deletable while nothing is closed)
    if (!last) {
      const osv = (await componentByCode(admin, 'OSVETLENI'))!;
      for (const e of await json<{ id: string; note: string | null }[]>(admin, `cost-components/${osv.id}/entries?from=2023-11-01&to=2027-12-31`))
        if (/^E2E-\S+ oprava$/.test(e.note ?? '')) await admin.delete(`cost-components/${osv.id}/entries/${e.id}?reason=cleanup`);
      for (const p of await json<Payment[]>(admin, `advances?houseId=${users.member.houseId}`))
        if (/E2E-\S+ (před uzávěrkou|pozdě)/.test(p.note ?? '')) await admin.delete(`advances/${users.member.houseId}/${enc(p.rowKey)}`);
    }
    // a payment inside the period that is about to be closed
    const pay = await admin.post('advances/doplatek', { data: { houseId: users.member.houseId, waterAmount: 111, electricityAmount: 0, commonAmount: 0, paymentDate: `${shift(C, -30)}T00:00:00Z`, note: `${TAG} před uzávěrkou` } });
    expect(pay.status(), await pay.text()).toBeLessThan(300);
    const me = await json<{ name: string }>(admin, 'auth/me');

    await loginAs(page, users.admin);
    await openClosings(page);
    const form = page.getByRole('form', { name: 'Nová mezizávěrka' });
    await expect(form.getByLabel('Uzavřít do (včetně)')).toHaveValue(shift(TODAY, -1));
    // today → refused
    await form.getByLabel('Uzavřít do (včetně)').fill(TODAY);
    await form.getByLabel('Důvod').fill(`${TAG} dnes`);
    await form.getByRole('button', { name: 'Uzavřít' }).click();
    await expect(form.getByRole('alert')).toBeVisible();

    await form.getByLabel('Uzavřít do (včetně)').fill(C);
    await form.getByLabel('Rozsah').selectOption('All');
    await form.getByLabel('Důvod').fill(`roční závěrka ${TAG}`);
    await form.getByRole('button', { name: 'Uzavřít' }).click();
    const detail = page.getByRole('region', { name: `Mezizávěrka k ${czDay(C)}` });
    await expect(detail).toBeVisible();
    await expect(detail).toContainText('Saldo k datu mezizávěrky odpovídá snímku.');
    const list = page.getByRole('table', { name: 'Seznam mezizávěrek' });
    const row = list.getByRole('row').filter({ hasText: `roční závěrka ${TAG}` });
    await expect(row).toContainText('všechny domy');
    await expect(row).toContainText(me.name);
    const all = await closings(admin);
    expect(all[0]).toMatchObject({ date: C, scope: 'All', canDelete: true });
    const snap = await json<Closing>(admin, `interim-closings/${enc(all[0].id)}`);
    expect(snap.houses!.length).toBe((await activeHouses(admin)).length);

    // not later than the existing one → refused
    await form.getByLabel('Uzavřít do (včetně)').fill(shift(C, -10));
    await form.getByLabel('Důvod').fill(`${TAG} dřív`);
    await form.getByRole('button', { name: 'Uzavřít' }).click();
    await expect(form.getByRole('alert')).toBeVisible();
  });

  test('náklad do uzavřeného období: bez důvodu odmítnut, s důvodem v UI se zaúčtuje jako oprava k prvnímu otevřenému dni', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const osv = (await componentByCode(admin, 'OSVETLENI'))!;
    await loginAs(page, users.admin);
    await go(page, '/naklady');
    await page.getByRole('tab', { name: osv.name, exact: true }).click();
    const form = page.getByRole('form', { name: 'Přidat náklad' });
    await form.getByLabel('Druh').selectOption('OneOff');
    await form.getByLabel('Období od').fill(shift(C, -60));
    await form.getByLabel('Částka (Kč)').fill('100');
    await form.getByRole('button', { name: 'Přidat' }).click();
    await expect(form.getByRole('alert')).toBeVisible();
    test.info().annotations.push({ type: 'evidence', description: `UI: ${await form.getByRole('alert').innerText()}` });
    await form.getByLabel(/Důvod/).fill(`${TAG} zapomenutá faktura`);
    await form.getByLabel('Poznámka').fill(`${TAG} oprava`);
    await form.getByRole('button', { name: 'Přidat' }).click();
    await expect(page.getByText(/oprava, zaúčtováno/).first()).toBeVisible();
    const corrections = (await json<{ note: string | null; postingDate: string | null }[]>(admin, `cost-components/${osv.id}/entries?from=${shift(C, -60)}&to=${shift(C, -60)}`))
      .filter((e) => e.note === `${TAG} oprava`);
    expect(corrections).toHaveLength(1);
    expect(corrections[0].postingDate).toBe(shift(C, 1));
    // the snapshot is unchanged (the correction is booked after the cut)
    const id = (await closings(admin))[0].id;
    const detail = await json<Closing>(admin, `interim-closings/${enc(id)}`);
    expect(detail.houses!.every((h) => h.difference === 0)).toBe(true);
  });

  test('platby po uzávěrce: smazání platby z uzavřeného období → 409 (hláška v UI); nová platba se zaúčtuje po řezu', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    await loginAs(page, users.admin);
    await go(page, '/saldo');
    const row = page.getByRole('row').filter({ hasText: `${TAG} před uzávěrkou` });
    await row.getByRole('button', { name: 'Smazat' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Smazat' }).click();
    await expect(page.locator('.bg-danger-light').first()).toBeVisible();
    test.info().annotations.push({ type: 'evidence', description: `UI: ${await page.locator('.bg-danger-light').first().innerText()}` });
    await expect(row).toHaveCount(1);

    const late = await admin.post('advances/doplatek', { data: { houseId: users.member.houseId, waterAmount: 222, electricityAmount: 0, commonAmount: 0, paymentDate: `${shift(C, -5)}T00:00:00Z`, note: `${TAG} pozdě` } });
    expect(late.status(), await late.text()).toBeLessThan(300);
    const saved = (await json<Payment[]>(admin, `advances?houseId=${users.member.houseId}`)).find((p) => p.note?.includes(`${TAG} pozdě`))!;
    expect(saved.paymentDate.slice(0, 10)).toBe(shift(C, 1));
    expect(saved.note).toMatch(/\d/); // original date noted
    test.info().annotations.push({ type: 'evidence', description: `pozdní platba: ${saved.paymentDate} „${saved.note}“` });
  });

  test('odečty, pravidla, účast, pokladna do uzavřeného období odmítnuty; počáteční stav jen s důvodem (oprava) a detail ukáže rozdíl', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const main = (await meters(admin)).find((m) => m.type === 'Main')!;
    const closedReading = (await json<Reading[]>(admin, 'readings/all')).filter((r) => r.meterId === main.id && r.readingDate.slice(0, 10) <= C).at(-1)!;
    const day = closedReading.readingDate.slice(0, 10);
    const results: Record<string, number> = {};
    results.readingPut = (await admin.put(`readings/${main.id}/${day}`, { data: { value: closedReading.value } })).status();
    results.readingPost = (await admin.post('readings', { data: { meterId: main.id, readingDate: `${shift(C, -3)}T00:00:00Z`, value: closedReading.value } })).status();
    const osv = (await componentByCode(admin, 'OSVETLENI'))!;
    results.rule = (await admin.post(`cost-components/${osv.id}/rules`, { data: { validFrom: shift(C, -1), method: 'Equal', reason: TAG } })).status();
    const { houses } = await roles(admin, users.member.houseId);
    results.participation = (await admin.post(`cost-components/${osv.id}/participations`, { data: { houseId: houses[0].id, validFrom: shift(C, -1), validTo: shift(C, -1) } })).status();
    results.cash = (await admin.post('cash-book', { data: { date: shift(C, -1), type: 'Deposit', amount: 1, category: 'x', description: TAG, hasReceipt: true } })).status();
    test.info().annotations.push({ type: 'evidence', description: JSON.stringify(results) });
    expect(results.readingPut).toBe(409);
    expect([400, 409]).toContain(results.readingPost);
    for (const k of ['rule', 'participation', 'cash']) expect([400, 409], k).toContain(results[k]);

    // opening fund share: without a reason refused, with a reason it is a correction → the closing shows a difference
    const balances = await json<{ key: string; type: string; houseId: string | null; value: number; date: string; source: string; isEstimate: boolean }[]>(admin, 'opening-balances');
    const fund = balances.find((b) => b.type === 'FundShare' && b.houseId === users.member.houseId)!;
    const body = { type: 'FundShare', houseId: fund.houseId, date: fund.date, value: fund.value + 100, isEstimate: false, source: fund.source };
    expect([400, 409]).toContain((await admin.put(`opening-balances/${enc(fund.key)}`, { data: body })).status());
    expect((await admin.put(`opening-balances/${enc(fund.key)}`, { data: { ...body, reason: `${TAG} oprava podílu` } })).status()).toBe(200);
    await loginAs(page, users.admin);
    await openClosings(page);
    await page.getByRole('table', { name: 'Seznam mezizávěrek' }).getByRole('row').filter({ hasText: `roční závěrka ${TAG}` }).getByRole('button', { name: 'Detail' }).click();
    const detail = page.getByRole('region', { name: `Mezizávěrka k ${czDay(C)}` });
    await expect(detail).toContainText('Saldo k datu mezizávěrky se od snímku změnilo — zkontrolujte rozdíly.');
    await expect(detail.getByRole('table', { name: 'Snímek salda' }).locator('td.bg-danger-light')).toHaveCount(1);
    // revert the correction (also with a reason)
    expect((await admin.put(`opening-balances/${enc(fund.key)}`, { data: { ...body, value: fund.value, reason: `${TAG} vráceno` } })).status()).toBe(200);
  });

  test('detail a export pro účetní (XLSX + CSV)', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    await loginAs(page, users.admin);
    await openClosings(page);
    await page.getByRole('table', { name: 'Seznam mezizávěrek' }).getByRole('row').filter({ hasText: `roční závěrka ${TAG}` }).getByRole('button', { name: 'Detail' }).click();
    const detail = page.getByRole('region', { name: `Mezizávěrka k ${czDay(C)}` });
    const x = await download(page, () => detail.getByRole('button', { name: 'Export pro účetní (XLSX)' }).click(), /xlsx/);
    expect(isXlsx(x.body)).toBe(true);
    const c = await download(page, () => detail.getByRole('button', { name: 'CSV' }).click(), /csv/);
    expect(isCzechCsv(c.body)).toBe(true);
    // the file name comes from Content-Disposition — readable cross-origin only when the API exposes the header
    const id = (await closings(admin)).find((z) => z.date === C)!.id;
    const res = await admin.get(`interim-closings/${enc(id)}/export?format=xlsx`, { headers: { Origin: APP } });
    const headers = res.headers();
    test.info().annotations.push({ type: 'evidence', description: `stažený soubor „${x.name}“, API Content-Disposition: ${headers['content-disposition']}, Access-Control-Expose-Headers: ${headers['access-control-expose-headers'] ?? '—'}` });
    if (C.endsWith('12-31')) {
      expect(headers['content-disposition']).toContain(`rocni-zaverka-${C.slice(0, 4)}.xlsx`);
      // BUG: prohlížeč jméno nevidí (CORS nevystavuje Content-Disposition) → všechny exporty se ukládají jako „saldo.*“
      test.fail(x.name !== `rocni-zaverka-${C.slice(0, 4)}.xlsx`, 'název exportu se v prohlížeči ztratí');
      expect(x.name).toBe(`rocni-zaverka-${C.slice(0, 4)}.xlsx`);
    }
  });

  test('účetní vidí mezizávěrky a detail, ale nezakládá ani neruší', async ({ page, playwright }) => {
    await loginAs(page, users.accountant);
    await openClosings(page);
    await expect(page.getByRole('form', { name: 'Nová mezizávěrka' })).toHaveCount(0);
    await page.getByRole('table', { name: 'Seznam mezizávěrek' }).getByRole('row').filter({ hasText: `roční závěrka ${TAG}` }).getByRole('button', { name: 'Detail' }).click();
    await expect(page.getByRole('region', { name: `Mezizávěrka k ${czDay(C)}` })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Zrušit mezizávěrku' })).toHaveCount(0);
    const acc = await api(playwright, users.accountant);
    expect((await acc.post('interim-closings', { data: { date: '2024-01-31', scope: 'All', reason: 'x' } })).status()).toBe(403);
  });

  test('zrušení poslední mezizávěrky s důvodem; pak jdou opravy smazat', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    await loginAs(page, users.admin);
    await openClosings(page);
    await page.getByRole('table', { name: 'Seznam mezizávěrek' }).getByRole('row').filter({ hasText: `roční závěrka ${TAG}` }).getByRole('button', { name: 'Detail' }).click();
    const detail = page.getByRole('region', { name: `Mezizávěrka k ${czDay(C)}` });
    await expect(detail.getByRole('button', { name: 'Zrušit mezizávěrku' })).toBeDisabled();
    await detail.getByLabel('Důvod zrušení').fill(`${TAG} test zrušení`);
    await detail.getByRole('button', { name: 'Zrušit mezizávěrku' }).click();
    await expect(page.getByRole('table', { name: 'Seznam mezizávěrek' }).getByRole('row').filter({ hasText: `roční závěrka ${TAG}` })).toHaveCount(0);
    expect((await closings(admin)).some((c) => c.reason === `roční závěrka ${TAG}`)).toBe(false);

    // cleanup of what this spec booked
    const osv = (await componentByCode(admin, 'OSVETLENI'))!;
    const entries = await json<{ id: string; note: string | null }[]>(admin, `cost-components/${osv.id}/entries?from=2023-11-01&to=2027-12-31`);
    for (const e of entries.filter((x) => x.note === `${TAG} oprava`)) expect((await admin.delete(`cost-components/${osv.id}/entries/${e.id}?reason=cleanup`)).status()).toBeLessThan(300);
    for (const p of (await json<Payment[]>(admin, `advances?houseId=${users.member.houseId}`)).filter((x) => x.note?.includes(TAG)))
      expect((await admin.delete(`advances/${users.member.houseId}/${enc(p.rowKey)}`)).status()).toBeLessThan(300);
  });
});

test.describe.serial('převod domu (T03 R7, S4)', () => {
  let houseId = '';
  let houseName = '';
  const D = shift(TODAY, -3);

  test('náhled dopadů a převod na nového vlastníka se stavem vodoměru', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const { others, lateJoiner } = await roles(admin, users.member.houseId);
    const periods = await json<{ houseId: string; validFrom: string }[]>(admin, 'ownership-periods');
    const latest = (id: string) => periods.filter((p) => p.houseId === id).map((p) => p.validFrom).sort().at(-1) ?? '9999';
    const count = (id: string) => periods.filter((p) => p.houseId === id).length;
    // any house except the member's and the late joiner whose current owner started before D (fewest transfers first)
    const candidate = others.filter((h) => h.id !== lateJoiner.id && latest(h.id) < D).sort((a, b) => count(a.id) - count(b.id))[0];
    test.skip(!candidate, 'žádný dům, jehož současný vlastník začal před datem převodu');
    houseId = candidate!.id;
    houseName = candidate!.name;
    const meter = (await meters(admin)).find((m) => m.houseId === houseId);

    await loginAs(page, users.admin);
    await go(page, '/admin/opening-balances');
    const section = page.getByRole('region', { name: 'Převod domu' });
    await section.getByLabel('Dům').selectOption({ label: houseName });
    await section.getByLabel('Nový vlastník od').fill(D);
    await section.getByRole('button', { name: 'Náhled dopadů' }).click();
    await expect(section.getByText('Uzavře se k')).toBeVisible();
    await expect(section).toContainText(czDay(shift(D, -1)));
    await expect(section).toContainText(/přeplatek|nedoplatek/);
    const form = section.getByRole('form', { name: 'Údaje nového vlastníka' });
    await form.getByLabel('Nový vlastník').fill(`Nový vlastník ${TAG}`);
    await form.getByLabel('Kontakt (e-mail)').fill('novy-vlastnik@example.invalid');
    if (meter) {
      await expect(form.getByLabel(`Stav vodoměru ${meter.meterNumber} (m³)`)).not.toHaveValue('');
      await form.getByLabel('Zdroj stavu').fill(`předávací protokol ${TAG}`);
    }
    await form.getByLabel('Podíl ve fondu (Kč)').fill('0');
    const contact = form.getByRole('checkbox', { name: 'změnit kontakt domu' });
    if (await contact.isChecked()) await contact.click(); // keep the real house contact untouched
    await form.getByRole('button', { name: 'Převést dům' }).click();
    await expect(section.getByRole('status')).toContainText('Dům převeden. Závěrečné saldo původního vlastníka:');

    const after = (await json<{ houseId: string; ownerName: string; validFrom: string; validTo: string | null }[]>(admin, 'ownership-periods')).filter((p) => p.houseId === houseId);
    expect(after.length).toBeGreaterThanOrEqual(2);
    expect(after.find((p) => p.validFrom === D)?.ownerName).toBe(`Nový vlastník ${TAG}`);
    const previous = after.filter((p) => p.validFrom < D).sort((a, b) => a.validFrom.localeCompare(b.validFrom)).at(-1)!;
    expect(previous.validTo).toBe(shift(D, -1));
    const house = (await activeHouses(admin)).find((h) => h.id === houseId)!;
    expect(house.email).not.toBe('novy-vlastnik@example.invalid');
    const c = (await closings(admin))[0];
    expect(c).toMatchObject({ scope: 'House', houseId, date: shift(D, -1) });
  });

  test('saldo domu: výběr období vlastnictví (starý a nový vlastník), nový začíná od data převodu', async ({ page, playwright }) => {
    test.skip(!houseId);
    const admin = await api(playwright, users.admin);
    const ledger = await json<{ ownershipPeriods: { id: string; ownerName: string; validFrom: string }[]; from: string; items: { date: string }[] }>(admin, `ledger/houses/${houseId}`);
    const n = ledger.ownershipPeriods.length;
    expect(n).toBeGreaterThanOrEqual(2);
    expect(ledger.from).toBe(D);
    expect(ledger.items.every((i) => i.date >= D)).toBe(true);
    await loginAs(page, users.admin);
    await go(page, '/saldo-domu');
    await page.getByRole('table', { name: 'Saldo domů' }).getByRole('row').filter({ hasText: houseName }).click();
    const detail = page.getByRole('region', { name: `Saldo ${houseName}` });
    await expect(detail).toContainText(`vlastník Nový vlastník ${TAG}`);
    const select = detail.getByLabel('Období vlastnictví');
    await expect(select.locator('option')).toHaveCount(n);
    const old = ledger.ownershipPeriods.filter((p) => p.validFrom < D).sort((a, b) => a.validFrom.localeCompare(b.validFrom)).at(-1)!;
    await select.selectOption(old.id);
    await expect(detail).not.toContainText(`vlastník Nový vlastník ${TAG}`);
    await expect(detail).toContainText(`– ${czDay(shift(D, -1))}`);
  });

  test('počáteční stavy po převodu: řádek vodoměru ukazuje stav nového vlastníka', async ({ page, playwright }) => {
    test.skip(!houseId);
    const admin = await api(playwright, users.admin);
    const meter = (await meters(admin)).find((m) => m.houseId === houseId);
    test.skip(!meter, 'dům nemá vodoměr');
    await loginAs(page, users.admin);
    await go(page, '/admin/opening-balances');
    const section = page.getByRole('region', { name: 'Stavy vodoměrů' });
    const row = section.locator('div.space-y-2').filter({ has: page.getByLabel(`Hodnota ${houseName}`, { exact: true }) });
    const text = await row.innerText();
    test.info().annotations.push({ type: 'evidence', description: text.replace(/\s+/g, ' ').slice(0, 200) });
    await expect(row).toContainText(`k ${czDay(D)}`);
  });

  test('mezizávěrka převodu (jen jeden dům) blokuje i ostatní domy a pokladnu? — zjištění; pak se zruší', async ({ page, playwright }) => {
    test.skip(!houseId);
    const admin = await api(playwright, users.admin);
    const cut = shift(D, -1);
    const other = (await meters(admin)).find((m) => m.houseId === users.member.houseId)!;
    const reading = (await json<Reading[]>(admin, 'readings/all')).filter((r) => r.meterId === other.id && r.readingDate.slice(0, 10) <= cut).at(-1)!;
    const results = {
      otherHouseReadingPut: (await admin.put(`readings/${other.id}/${reading.readingDate.slice(0, 10)}`, { data: { value: reading.value } })).status(),
      cashBookBeforeCut: (await admin.post('cash-book', { data: { date: cut, type: 'Deposit', amount: 1, category: 'x', description: `${TAG} po převodu`, hasReceipt: true } })).status(),
    };
    test.info().annotations.push({ type: 'evidence', description: JSON.stringify(results) });
    // cleanup of a deposit that got through
    if (results.cashBookBeforeCut === 201) {
      const book = await json<{ entries: { id: string; description: string }[] }>(admin, 'cash-book');
      const e = book.entries.find((x) => x.description === `${TAG} po převodu`);
      if (e) await admin.post(`cash-book/${e.id}/storno`, { data: { reason: 'cleanup' } });
    }

    // delete the transfer's house closing (the latest) so the suite stays re-runnable
    await loginAs(page, users.admin);
    await openClosings(page);
    const row = page.getByRole('table', { name: 'Seznam mezizávěrek' }).getByRole('row').filter({ hasText: houseName });
    await row.getByRole('button', { name: 'Detail' }).click();
    const detail = page.getByRole('region', { name: `Mezizávěrka k ${czDay(cut)}` });
    await detail.getByLabel('Důvod zrušení').fill(`${TAG} úklid po testu převodu`);
    await detail.getByRole('button', { name: 'Zrušit mezizávěrku' }).click();
    await expect(row).toHaveCount(0);
    expect(await lastClosedDay(admin)).toBeNull();
  });
});
