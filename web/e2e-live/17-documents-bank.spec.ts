import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { Page } from '@playwright/test';
import { RUN, api, expect, go, loginAs, test, users } from './live';
import { activeHouses, componentByCode, download, isPdf, isXlsx, json, pdf } from './data';

/**
 * Dokumenty (kategorie, verze, stažení, mazání), faktury T11 (hromadné nahrání se složkou → „Dokumenty bez zaúčtování“
 * → „Vytvořit náklad“ s předvyplněným dokladem) a import bankovního výpisu (náhled, potvrzení, opakované nahrání).
 */
const TAG = `${RUN}-${Date.now().toString(36).slice(-4)}`;
interface Doc { id: string; name: string; category: string; componentId: string | null }
interface Unaccounted { document: Doc; componentName: string | null }

async function openDocs(page: Page) {
  await go(page, '/documents');
  await expect(page.getByRole('heading', { name: 'Dokumenty', exact: true })).toBeVisible();
  await expect(page.locator('.animate-spin')).toHaveCount(0, { timeout: 30_000 });
}

test.describe.serial('dokumenty a faktury (T11)', () => {
  test('stažený dokument má obsah (velikost = uložená velikost), nahraný i dříve existující', async ({ playwright }) => {
    const admin = await api(playwright, users.admin);
    // leftovers of interrupted earlier runs
    for (const d of await json<Doc[]>(admin, 'documents'))
      if (/^(Zapis ze schuze|Zápis ze schůze|Zapis|obsah|velký) E2E-/.test(d.name) || /^faktura-b-E2E-/.test(d.name)) await admin.delete(`documents/${d.id}`);
    const res = await admin.post(`documents?name=${encodeURIComponent(`obsah ${TAG}`)}&category=ostatni`, { data: pdf(`obsah ${TAG}`), headers: { 'Content-Type': 'application/pdf' } });
    expect(res.status()).toBe(201);
    const created = (await res.json()) as Doc;
    const docs = await json<(Doc & { fileSizeBytes: number })[]>(admin, 'documents');
    const report: string[] = [];
    let bad = 0;
    for (const d of docs) {
      const r = await admin.get(`documents/${d.id}/download`);
      const len = r.status() === 200 ? (await r.body()).length : -1;
      report.push(`${d.category}/${d.id.slice(0, 8)}: ${r.status()} ${len} B z ${d.fileSizeBytes} B`);
      if (r.status() === 200 && len !== d.fileSizeBytes) bad++;
    }
    await admin.delete(`documents/${created.id}`);
    test.info().annotations.push({ type: 'evidence', description: report.join('; ') });
    expect(bad, report.join('\n')).toBe(0);
  });

  test('roční přehled hospodaření: export PDF a XLSX má obsah', async ({ playwright }) => {
    // BUG: FinanceFunctions export/pdf a export/xlsx používají stejný vzor (response.Body = new MemoryStream) → 0 B.
    const admin = await api(playwright, users.admin);
    const pdfRes = await admin.get('finance/export/pdf?year=2026');
    const xlsxRes = await admin.get('finance/export/xlsx?year=2026');
    const p = await pdfRes.body();
    const x = await xlsxRes.body();
    test.info().annotations.push({ type: 'evidence', description: `PDF ${pdfRes.status()} ${p.length} B, XLSX ${xlsxRes.status()} ${x.length} B` });
    test.fail(p.length === 0 || x.length === 0, 'export hospodaření vrací prázdný soubor');
    expect(pdfRes.status()).toBe(200);
    expect(isPdf(p)).toBe(true);
    expect(isXlsx(x)).toBe(true);
  });

  test('dokument s diakritikou v názvu jde stáhnout (aktuální i starší verze)', async ({ playwright }) => {
    const admin = await api(playwright, users.admin);
    const upload = async (name: string) => {
      const res = await admin.post(`documents?name=${encodeURIComponent(name)}&category=ostatni`, { data: pdf(name), headers: { 'Content-Type': 'application/pdf' } });
      expect(res.status(), await res.text()).toBe(201);
      return (await res.json()) as Doc;
    };
    const ascii = await upload(`Zapis ${TAG}`);
    const czech = await upload(`Zápis ze schůze ${TAG}`);
    try {
      const a = await admin.get(`documents/${ascii.id}/download`);
      const c = await admin.get(`documents/${czech.id}/download`);
      test.info().annotations.push({ type: 'evidence', description: `ASCII název → ${a.status()}, „Zápis ze schůze…“ → ${c.status()}` });
      expect(a.status()).toBe(200);
      expect(c.status()).toBe(200);
    } finally {
      await admin.delete(`documents/${ascii.id}`);
      await admin.delete(`documents/${czech.id}`);
    }
  });

  test('nahrání zápisu (PDF), stažení, nová verze, historie verzí, smazání', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    await loginAs(page, users.admin);
    await openDocs(page);
    await page.getByRole('button', { name: 'Nahrát dokument' }).click();
    const modal = page.getByRole('dialog');
    await modal.getByLabel('Název dokumentu').fill(`Zapis ze schuze ${TAG}`);
    await modal.getByLabel('Kategorie').selectOption('zapisy');
    await modal.locator('input[type=file]').setInputFiles({ name: 'zapis.pdf', mimeType: 'application/pdf', buffer: pdf(`zapis ${TAG} v1`) });
    await modal.getByRole('button', { name: 'Nahrát', exact: true }).click();
    await expect(modal).toHaveCount(0);
    const row = page.getByRole('row').filter({ hasText: `Zapis ze schuze ${TAG}` });
    await expect(row).toContainText('Zápisy');

    const stored = (await json<(Doc & { fileSizeBytes: number })[]>(admin, 'documents')).find((d) => d.name === `Zapis ze schuze ${TAG}`)!;
    expect(stored.fileSizeBytes).toBe(pdf(`zapis ${TAG} v1`).length);
    // the download starts with the document's name (its content is checked by the test above — currently empty, bug)
    const v1 = await download(page, () => row.getByRole('button', { name: 'Stáhnout' }).click(), /pdf/);
    expect(v1.name).toContain(`Zapis ze schuze ${TAG}`);

    await row.getByRole('button', { name: 'Nová verze' }).click();
    const vmodal = page.getByRole('dialog');
    await vmodal.locator('input[type=file]').setInputFiles({ name: 'zapis-v2.pdf', mimeType: 'application/pdf', buffer: pdf(`zapis ${TAG} v2`) });
    await vmodal.getByRole('button', { name: 'Nahrát verzi' }).click();
    await expect(vmodal).toHaveCount(0);
    await row.getByRole('button', { name: 'Zobrazit' }).click();
    const versions = page.getByRole('row').filter({ hasText: /^v\d/ });
    // the history lists the replaced (older) versions; the current file is the row's own download
    await expect(versions).toHaveCount(1);
    const versionList = await json<{ versionNumber: number; fileSizeBytes: number }[]>(admin, `documents/${stored.id}/versions`);
    expect(versionList.map((v) => v.versionNumber)).toEqual([1]);
    await download(page, () => row.getByRole('button', { name: 'Stáhnout' }).click(), /pdf/);
    const old = await admin.get(`documents/${stored.id}/versions/1/download`);
    expect(old.status()).toBe(200);

    // category filter
    await page.getByRole('navigation', { name: 'Kategorie' }).getByRole('button', { name: 'Stanovy' }).click();
    await expect(page.getByRole('row').filter({ hasText: `Zapis ze schuze ${TAG}` })).toHaveCount(0);
    await page.getByRole('navigation', { name: 'Kategorie' }).getByRole('button', { name: 'Zápisy' }).click();
    await expect(row).toBeVisible();

    // member sees and downloads, but cannot change
    const memberPage = await page.context().browser()!.newPage();
    await loginAs(memberPage, users.member);
    await openDocs(memberPage);
    const mrow = memberPage.getByRole('row').filter({ hasText: `Zapis ze schuze ${TAG}` });
    await expect(mrow).toBeVisible();
    await expect(mrow.getByRole('button', { name: 'Smazat' })).toHaveCount(0);
    await expect(memberPage.getByRole('button', { name: 'Nahrát dokument' })).toHaveCount(0);
    const m = await download(memberPage, () => mrow.getByRole('button', { name: 'Stáhnout' }).click(), /pdf/);
    expect(m.name).toContain(`Zapis ze schuze ${TAG}`);
    await memberPage.close();

    await row.getByRole('button', { name: 'Smazat' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Smazat' }).click();
    await expect(row).toHaveCount(0);
    expect((await json<Doc[]>(admin, 'documents')).some((d) => d.name === `Zapis ze schuze ${TAG}`)).toBe(false);
  });

  test('hromadné nahrání faktur se složkou → „Dokumenty bez zaúčtování“ → „Vytvořit náklad“ s předvyplněným dokladem', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const el = (await componentByCode(admin, 'ELEKTRINA_VODARNA'))!;
    await loginAs(page, users.admin);
    await openDocs(page);
    await page.getByText('Nahrát faktury a vyúčtování (více souborů najednou)').click();
    const bulk = page.locator('[aria-label="Hromadné nahrání faktur"]');
    await bulk.locator('input[type=file]').setInputFiles([
      { name: `faktura-a-${TAG}.pdf`, mimeType: 'application/pdf', buffer: pdf('faktura A') },
      { name: `faktura-b-${TAG}.pdf`, mimeType: 'application/pdf', buffer: pdf('faktura B') },
      { name: `smlouva-${TAG}.docx`, mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', buffer: Buffer.from('PK\u0003\u0004 docx') },
    ]);
    await bulk.getByLabel('Nákladová složka (nepovinné)').selectOption({ label: el.name });
    await bulk.getByRole('button', { name: 'Nahrát 3' }).click();
    const results = bulk.getByRole('list', { name: 'Výsledek nahrání' });
    await expect(results.getByRole('listitem')).toHaveCount(3);
    await expect(results.getByRole('listitem').filter({ hasText: `faktura-a-${TAG}.pdf` })).toContainText('nahráno');
    await expect(results.getByRole('listitem').filter({ hasText: `faktura-b-${TAG}.pdf` })).toContainText('nahráno');
    await expect(results.getByRole('listitem').filter({ hasText: `smlouva-${TAG}.docx` })).not.toContainText('nahráno');
    test.info().annotations.push({ type: 'evidence', description: `DOCX do faktur: ${await results.getByRole('listitem').filter({ hasText: 'smlouva' }).innerText()}` });

    const unaccounted = await json<Unaccounted[]>(admin, 'documents/unaccounted');
    const a = unaccounted.find((u) => u.document.name === `faktura-a-${TAG}`)!;
    const b = unaccounted.find((u) => u.document.name === `faktura-b-${TAG}`)!;
    expect(a.componentName).toBe(el.name);
    expect(b).toBeTruthy();

    // Documents → „Vytvořit náklad“ opens Costs with component + document prefilled
    await page.getByRole('navigation', { name: 'Kategorie' }).getByRole('button', { name: 'Faktury a vyúčtování' }).click();
    await page.getByRole('row').filter({ hasText: `faktura-a-${TAG}` }).getByRole('link', { name: 'Vytvořit náklad' }).click();
    await expect(page).toHaveURL(new RegExp(`/naklady\\?.*document=${a.document.id}`));
    await expect(page.getByRole('tab', { name: el.name, exact: true })).toHaveAttribute('aria-selected', 'true');
    const list = page.getByRole('region', { name: 'Dokumenty bez zaúčtování' });
    await expect(list).toContainText(`faktura-a-${TAG}`);
    await expect(list).toContainText(el.name);
    const form = page.getByRole('form', { name: 'Přidat náklad' });
    await expect(form.getByLabel('Doklad (PDF v Dokumentech)')).toHaveValue(a.document.id);
    await form.getByLabel('Druh').selectOption('OneOff');
    await form.getByLabel('Období od').fill('2026-09-01');
    await form.getByLabel('Částka (Kč)').fill('1 234');
    await form.getByLabel('Poznámka').fill(`${TAG} z faktury`);
    await form.getByRole('button', { name: 'Přidat' }).click();
    await expect(form.getByRole('alert')).toHaveCount(0);
    await expect(list).not.toContainText(`faktura-a-${TAG}`);
    await expect(list).toContainText(`faktura-b-${TAG}`);
    // the entry shows the document and downloads it
    const entryRow = page.getByRole('table', { name: 'Nákladové záznamy' }).getByRole('row').filter({ hasText: `faktura-a-${TAG}` });
    const d = await download(page, () => entryRow.getByRole('button', { name: `faktura-a-${TAG}` }).click(), /pdf/);
    expect(d.name).toContain(`faktura-a-${TAG}`);
    // „Zaúčtovat“ from the list preselects the other invoice
    await list.getByRole('listitem').filter({ hasText: `faktura-b-${TAG}` }).getByRole('button', { name: 'Zaúčtovat' }).click();
    await expect(page.getByRole('form', { name: 'Přidat náklad' }).getByLabel('Doklad (PDF v Dokumentech)')).toHaveValue(b.document.id);

    // cleanup: the unused invoice and the cost entry with its document
    const docs = await json<Doc[]>(admin, 'documents');
    for (const doc of docs.filter((x) => x.name === `faktura-b-${TAG}`)) await admin.delete(`documents/${doc.id}`);
    expect((await json<Unaccounted[]>(admin, 'documents/unaccounted')).some((u) => u.document.name.includes(TAG))).toBe(false);
  });

  test('soubor nad 20 MB: uživatel dostane hlášku (ne tiché ignorování)', async ({ page }) => {
    await loginAs(page, users.admin);
    await openDocs(page);
    await page.getByRole('button', { name: 'Nahrát dokument' }).click();
    const modal = page.getByRole('dialog');
    await modal.getByLabel('Název dokumentu').fill(`velký ${TAG}`);
    await modal.locator('input[type=file]').setInputFiles({ name: 'velky.pdf', mimeType: 'application/pdf', buffer: Buffer.alloc(21 * 1024 * 1024, 0x20) });
    await expect(modal.getByText('velky.pdf')).toHaveCount(0); // the file was not taken
    await expect(modal.locator('.bg-danger-light, [role=alert]')).toBeVisible({ timeout: 5_000 });
  });

  test('účetní vidí „Dokumenty bez zaúčtování“ bez tlačítka Zaúčtovat; člen je nevidí vůbec (403)', async ({ playwright }) => {
    const acc = await api(playwright, users.accountant);
    expect((await acc.get('documents/unaccounted')).status()).toBe(200);
    const member = await api(playwright, users.member);
    expect((await member.get('documents/unaccounted')).status()).toBe(403);
    expect((await member.post('documents?name=x&category=ostatni', { data: pdf('x'), headers: { 'Content-Type': 'application/pdf' } })).status()).toBe(403);
  });
});

test.describe.serial('import z banky (Fio CSV)', () => {
  const fixture = readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), '../../api/tests/Oaza.Application.Tests/BankImport/Fixtures/fio-vypis-anonym.csv'));

  test('náhled výpisu → přiřazení domácnosti ručně → potvrzení; opakované nahrání nic neduplikuje', async ({ page, playwright }) => {
    const admin = await api(playwright, users.admin);
    const house = (await activeHouses(admin)).find((h) => h.id === users.member.houseId)!;
    await loginAs(page, users.admin);
    await go(page, '/advances/import');
    await expect(page.getByRole('heading', { name: 'Import z banky' })).toBeVisible();
    await page.locator('input[type=file]').setInputFiles({ name: 'vypis.csv', mimeType: 'text/csv', buffer: fixture });
    await page.getByRole('button', { name: 'Načíst náhled' }).click();
    await expect(page.getByText('2601634649/2010')).toBeVisible();
    await expect(page.getByText(/12\s?500,00\s?Kč/).first()).toBeVisible();
    const table = page.getByRole('table');
    const rows = table.locator('tbody tr');
    const total = await rows.count();
    expect(total).toBeGreaterThan(3);
    const editable = rows.filter({ has: page.getByRole('button', { name: /^(Ignorovat|Importovat)$/ }) });
    const n = await editable.count();
    if (n > 0) {
      // rows without a matched house block the confirmation (first run; later the account is already learned)
      if (await page.getByText('Vyberte domácnost, nebo platbu ignorujte.').count() > 0)
        await expect(page.getByRole('button', { name: 'Potvrdit import' })).toBeDisabled();
      // first new row → member's house as a doplatek; the rest ignored
      const first = editable.first();
      await first.getByRole('combobox').first().selectOption({ label: house.name });
      await first.getByRole('combobox').nth(1).selectOption('Doplatek');
      for (let i = 1; i < n; i++) {
        const r = editable.nth(i);
        if ((await r.getByRole('button', { name: 'Ignorovat' }).count()) > 0) await r.getByRole('button', { name: 'Ignorovat' }).click();
      }
      await expect(page.getByRole('button', { name: 'Potvrdit import' })).toBeEnabled();
      await page.getByRole('button', { name: 'Potvrdit import' }).click();
      await expect(page.getByText(/Naimportováno 1, ignorováno \d+/)).toBeVisible();
      await page.getByRole('button', { name: 'Importovat další výpis' }).click();
      await page.locator('input[type=file]').setInputFiles({ name: 'vypis.csv', mimeType: 'text/csv', buffer: fixture });
      await page.getByRole('button', { name: 'Načíst náhled' }).click();
    }
    // after processing, nothing is new any more
    await expect(page.getByText('Ve výpisu nejsou žádné nové příchozí platby.')).toBeVisible();
    await expect(page.getByText('Již importováno').first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'Potvrdit import' })).toBeDisabled();

    // the imported payment carries the „Z banky“ badge; deleting it releases the bank movement for a re-run
    const payments = await json<{ houseId: string; rowKey: string; isFromBank: boolean; paymentDate: string }[]>(admin, `advances?houseId=${house.id}`);
    const fromBank = payments.filter((p) => p.isFromBank && p.paymentDate.startsWith('2026-08'));
    expect(fromBank.length).toBeGreaterThan(0);
    for (const p of fromBank) expect((await admin.delete(`advances/${house.id}/${encodeURIComponent(p.rowKey)}`)).status()).toBeLessThan(300);
  });

  test('neplatný soubor (ne CSV z Fio) → srozumitelná chyba, žádný pád', async ({ page, allowProblems }) => {
    allowProblems(/HTTP 4\d\d/);
    await loginAs(page, users.admin);
    await go(page, '/advances/import');
    await page.locator('input[type=file]').setInputFiles({ name: 'nesmysl.csv', mimeType: 'text/csv', buffer: Buffer.from('a;b;c\n1;2;3\n') });
    await page.getByRole('button', { name: 'Načíst náhled' }).click();
    await expect(page.locator('.bg-danger-light')).toBeVisible();
  });
});
