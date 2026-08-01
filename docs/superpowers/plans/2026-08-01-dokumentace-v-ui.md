# Dokumentace chování aplikace v UI — implementační plán

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Aplikace bude sama vysvětlovat, jak funguje — vrstvená nápověda na obrazovkách plus stránka „Jak to funguje", obojí z jednoho zdroje textů.

**Architecture:** Jeden datový modul `web/src/content/help.ts` drží všechny texty jako typovaná data. Tři prezentační komponenty (`HelpNote`, `HelpDisclosure`, `HelpTerm`) je konzumují podle klíče. Stránka `/jak-to-funguje` renderuje tentýž modul, takže se kontextová nápověda a centrální stránka nemohou rozejít.

**Tech Stack:** React 19, TypeScript 5.9 (strict), Vite 8, TailwindCSS 4, lucide-react. Žádná nová npm závislost.

**Zdroj textů:** `docs/superpowers/specs/2026-08-01-dokumentace-v-ui-design.md` §8. Plán texty **neduplikuje** — odkazuje na spec. Je to záměr: duplikace by založila přesně ten dvojí zdroj pravdy, kvůli kterému návrh vznikl.

---

## Global Constraints

- **Žádný test runner ve frontendu.** `web/package.json` má jen `dev`, `build`, `lint`, `preview`. Nepiš vitest/jest testy — spadly by na „command not found". Kontrolní cyklus každé úlohy je `npx tsc -b` → `npm run lint` → `npm run build` → ruční ověření v prohlížeči. Kde to jde, je použit **red-green přes typovou kontrolu**: union typy `TermId`/`SectionId` způsobí, že překlep v id neprojde `tsc -b`.
- **`erasableSyntaxOnly: true`** (`tsconfig.app.json`) — `enum` je zakázaný. Union string literaly nejsou stylová volba, ale jediná varianta, která se přeloží.
- **`verbatimModuleSyntax: true`** — každý typový import musí být `import type { X } from '…'`. Vzory: `Layout.tsx:24`, `MetricCard.tsx:1`.
- **`noUnusedLocals: true`** — nepoužitá importovaná ikona shodí `npm run build`.
- **`react-refresh/only-export-components` = error** s `allowConstantExport: true`. V `components/help/*.tsx` smí vedle komponenty stát exportovaná **konstanta**, ale ne exportovaná **funkce**. Pomocné funkce patří do `content/help.ts`.
- **Žádný barrel `index.ts`.** Projekt importuje komponenty přímo (`'../components/ConfirmDialog'`). Drž se toho: `import { HelpNote } from '../components/help/HelpNote';`
- **Konvence komponent** (vzor `Spinner.tsx`): neexportovaný `interface XProps` nad komponentou; `export function X(…)`, nikdy default export; modulová konstanta typu `Record` pro varianty; className skládaný template literálem; aria atributy česky.
- **Barevné tokeny** jsou v `web/src/index.css` ř. 3–51 (Tailwind v4 `@theme`): `text-text-secondary`, `text-text-muted`, `bg-surface-raised`, `bg-surface-sunken`, `text-accent`, `border-border`.
- **`HelpNote` nejvýše 3 věty.** Seznamy a delší obsah povinně do `HelpDisclosure`.
- **Commituj po každé úloze.** Conventional commits anglicky (`feat:`, `refactor:`, `docs:`).

---

## File Structure

| Soubor | Odpovědnost |
|---|---|
| `web/src/content/help.ts` | **Nový.** Typy `TermId`/`SectionId`, data `terms`/`sections`/`guides`. Žádný JSX. |
| `web/src/components/help/HelpNote.tsx` | **Nový.** Vždy viditelná věta pod nadpisem sekce. |
| `web/src/components/help/HelpDisclosure.tsx` | **Nový.** Nativní `<details>`/`<summary>` s plným výkladem. |
| `web/src/components/help/HelpTerm.tsx` | **Nový.** `(?)` u pojmu s popoverem. |
| `web/src/pages/JakToFungujePage.tsx` | **Nový.** Renderuje `guides` + slovník ze `terms`. |
| `web/src/components/ConfirmDialog.tsx` | **Změna.** `message: string` → `ReactNode`, `<p>` → `<div>`. |
| `web/src/pages/BillingPage.tsx` | **Změna.** Tier 1 — nejvíc kotev v celém plánu. |
| `web/src/pages/SaldoPage.tsx` | **Změna.** Tier 2. |
| `web/src/pages/AdvancesPage.tsx` | **Změna.** `lossMethod` u read-only dlaždice. |
| `web/src/pages/admin/HousesPage.tsx`, `admin/UsersPage.tsx`, `ReadingsImportPage.tsx`, `DocumentsPage.tsx`, `InvoicesOverviewPage.tsx`, `components/InvoicesSection.tsx` | **Změna.** Tier 2/3, jedna až dvě kotvy každý. |
| `web/src/App.tsx`, `web/src/components/Layout.tsx` | **Změna.** Route a navigační položka. |

**Pozor na čísla řádků:** všechna níže jsou ověřená čtením souborů k 2026-08-01 (`BillingPage.tsx` = 1348 řádků, `SaldoPage.tsx` = 581, `AdvancesPage.tsx` = 383). Každá úloha posune řádky pro následující — **vždy hledej podle doslovného úryvku, ne podle čísla.**

---

## Dávka A — infrastruktura a Tier 1

### Task 1: Obsahový modul

**Files:**
- Create: `web/src/content/help.ts`

**Interfaces:**
- Produces: `TermId`, `SectionId` (union typy), `Term`, `Section`, `Guide` (interface), `terms: Record<TermId, Term>`, `sections: Record<SectionId, Section>`, `guides: Guide[]`

- [ ] **Step 1: Založ modul s typy a kostrou**

```ts
/**
 * Jediný zdroj textů nápovědy. Konzumují ho jak kontextové komponenty
 * (HelpNote / HelpDisclosure / HelpTerm), tak stránka /jak-to-funguje —
 * proto se nemohou rozejít.
 *
 * Texty popisující výpočty pocházejí z těchto use casů; při jejich změně
 * uprav i text zde:
 *   billingAdmin, billingMember, castka, podil, ztrata  → CalculateSettlementUseCase
 *   fundDraw, waterPriceCarry                           → CloseBillingPeriodUseCase
 *   cistyZustatek, upravy                               → CalculateHouseSaldoUseCase
 */

export type TermId =
  | 'ztrata' | 'podil' | 'castka' | 'zalohy' | 'vysledek'
  | 'zFondu' | 'finalniSaldo' | 'upravy' | 'cistyZustatek'
  | 'rozpoustiPreplatek' | 'role' | 'chybiOdecet' | 'anomalie';

export type SectionId =
  | 'billingAdmin' | 'billingMember' | 'fundDraw' | 'waterPriceCarry'
  | 'closedSnapshot' | 'saldoLive' | 'lossMethod' | 'paymentTypes'
  | 'importTwoStep' | 'invoiceLineItems' | 'documentVersions'
  | 'receivedInvoicesFund';

export interface Term {
  label: string;
  short: string;
  long?: string;
}

export interface Section {
  /** Chybí u sekcí, které mají jen rozklikávačku — dnes jedině `lossMethod`. */
  note?: string;
  disclosureTitle?: string;
  disclosure?: string;
}

export interface Guide {
  id: string;
  title: string;
  body: string[];
}

export const terms: Record<TermId, Term> = {
  ztrata: {
    label: 'Ztráta m³',
    short: 'Voda, kterou naměřil hlavní vodoměr, ale žádný domovní. Vzniká úniky na společném řadu a měřicí nepřesností.',
    long: 'Ztráta = spotřeba hlavního vodoměru minus součet spotřeb všech domácností za období. Zaplatit ji musí společenství, takže se rozděluje mezi domácnosti — buď rovným dílem, nebo podle spotřeby. Metodu vybíráte u zúčtovacího období. Záporná ztráta znamená, že domovní vodoměry naměřily víc než hlavní; to signalizuje chybu v odečtech nebo výměnu vodoměru, ne úsporu.',
  },
  podil: {
    label: 'Podíl %',
    short: 'Jaký díl celkového nákladu za vodu připadá na tuto domácnost.',
    long: 'Podíl = (spotřeba domácnosti + přidělená ztráta) ÷ (celková spotřeba všech domácností + celá ztráta), v procentech. Počítá se tedy ze spotřeby včetně ztráty, ne ze samotné naměřené spotřeby. Součet podílů všech domácností v období dá 100 %.',
  },
  // … zbylých 11 pojmů
};

export const sections: Record<SectionId, Section> = {
  billingAdmin: {
    note: 'Zúčtovací období je časový úsek, za který se rozpočítá voda mezi domácnosti. Dokud je otevřené, můžete do něj přidávat faktury a odečty a průběžně si nechat spočítat náhled.',
    disclosureTitle: 'Jak se počítá vyúčtování',
    disclosure: '…',
  },
  // … zbylých 11 sekcí
};

export const guides: Guide[] = [];  // naplní Task 11
```

- [ ] **Step 2: Doplň zbylých 11 pojmů a 11 sekcí doslova ze specifikace**

Otevři `docs/superpowers/specs/2026-08-01-dokumentace-v-ui-design.md`, sekce **§8.1** (pojmy) a **§8.2** (sekce). Mapování je mechanické:

- tučný identifikátor v backticích (`**\`ztrata\` — Ztráta m³**`) → klíč objektu + `label`
- odstavec označený *short:* → `short`
- odstavec označený *long:* → `long` (víceodstavcové texty spoj do jednoho řetězce s `\n\n`)
- odstavec označený *note:* → `note`
- nadpis za *disclosure —* → `disclosureTitle`, tělo → `disclosure`

Texty **neupravuj ani nezkracuj** — jsou schválené včetně interpunkce. České uvozovky `„ "`, `m³`, `÷`, `×` a `—` zachovej.

`guides` nech prozatím prázdné pole; naplní ho Task 11.

- [ ] **Step 3: Ověř, že se modul přeloží**

Run: `cd web && npx tsc -b`
Expected: PASS, žádný výstup.

- [ ] **Step 4: Dokaž, že typová pojistka funguje (red-green)**

Dočasně přidej na konec souboru:

```ts
const _guard: Term = terms['neexistujici' as TermId];
```

Run: `cd web && npx tsc -b`
Expected: **FAIL** — `Property 'neexistujici' does not exist` / `Type '"neexistujici"' is not assignable to type 'TermId'`.

Tohle je jediná automatická pojistka, kterou v tomto projektu máme. Když **neselže**, jsou union typy špatně napsané (typicky `string` místo literálů) — oprav je, než pokračuješ.

- [ ] **Step 5: Odstraň dočasný guard a ověř zeleno**

Run: `cd web && npx tsc -b && npm run lint`
Expected: obojí PASS.

- [ ] **Step 6: Commit**

```bash
git add web/src/content/help.ts
git commit -m "feat(web): help content module with typed term and section keys"
```

---

### Task 2: Prezentační komponenty

**Files:**
- Create: `web/src/components/help/HelpNote.tsx`
- Create: `web/src/components/help/HelpDisclosure.tsx`
- Create: `web/src/components/help/HelpTerm.tsx`

**Interfaces:**
- Consumes: `terms`, `sections`, `TermId`, `SectionId` z `web/src/content/help.ts`
- Produces: `<HelpNote sectionId={SectionId} />`, `<HelpDisclosure sectionId={SectionId} />`, `<HelpTerm id={TermId} />`

- [ ] **Step 1: HelpNote**

```tsx
import type { SectionId } from '../../content/help';
import { sections } from '../../content/help';

interface HelpNoteProps {
  sectionId: SectionId;
}

export function HelpNote({ sectionId }: HelpNoteProps) {
  const note = sections[sectionId].note;
  if (!note) return null;

  return <p className="mt-1 text-sm text-text-secondary">{note}</p>;
}
```

Guard je nutný: `note` je volitelný, protože `lossMethod` má jen rozklikávačku. Bez něj by se vykreslil prázdný odstavec s odsazením.

- [ ] **Step 2: HelpDisclosure**

Víceodstavcový text se dělí na `\n\n`. `<details>` je nativní — žádný `useState`, takže na něj React Compiler lint nemá jak sáhnout.

```tsx
import type { SectionId } from '../../content/help';
import { sections } from '../../content/help';

interface HelpDisclosureProps {
  sectionId: SectionId;
}

export function HelpDisclosure({ sectionId }: HelpDisclosureProps) {
  const section = sections[sectionId];
  if (!section.disclosure) return null;

  return (
    <details className="mt-2 rounded-xl border border-border bg-surface-sunken px-3 py-2">
      <summary className="cursor-pointer text-sm font-medium text-accent">
        {section.disclosureTitle ?? 'Jak to funguje'}
      </summary>
      <div className="mt-2 space-y-2 text-sm text-text-secondary">
        {section.disclosure.split('\n\n').map((para, i) => (
          <p key={i}>{para}</p>
        ))}
      </div>
    </details>
  );
}
```

- [ ] **Step 3: HelpTerm**

Také `<details>` — vyhne se to stavu i knihovně na popovery. `inline` varianta `<details>` se stylizuje jako značka za textem.

```tsx
import type { TermId } from '../../content/help';
import { terms } from '../../content/help';

interface HelpTermProps {
  id: TermId;
}

export function HelpTerm({ id }: HelpTermProps) {
  const term = terms[id];

  return (
    <details className="relative inline-block align-middle">
      <summary
        className="ml-1 cursor-pointer list-none rounded-full border border-border px-1.5 text-xs text-text-muted hover:text-accent"
        aria-label={`Nápověda: ${term.label}`}
      >
        ?
      </summary>
      <div className="absolute right-0 z-20 mt-1 w-64 rounded-xl border border-border bg-surface-raised p-3 text-left text-xs font-normal normal-case tracking-normal text-text-secondary shadow-dialog">
        <strong className="block text-text-primary">{term.label}</strong>
        <span className="mt-1 block">{term.short}</span>
        {term.long && (
          <a href={`/jak-to-funguje#${id}`} className="mt-2 block text-accent hover:underline">
            Více
          </a>
        )}
      </div>
    </details>
  );
}
```

`normal-case tracking-normal font-normal text-left` je nutné: komponenta se vkládá do `<th>`, které má `uppercase tracking-wider font-semibold text-right` — bez resetu by popover zdědil verzálky.

- [ ] **Step 4: Ověř překlad a lint**

Run: `cd web && npx tsc -b && npm run lint`
Expected: obojí PASS. Pokud lint hlásí `react-refresh/only-export-components`, exportuješ ze souboru vedle komponenty ještě funkci — přesuň ji do `content/help.ts`.

- [ ] **Step 5: Commit**

```bash
git add web/src/components/help/
git commit -m "feat(web): HelpNote, HelpDisclosure and HelpTerm components"
```

---

### Task 3: ConfirmDialog přijme ReactNode

Nutná podmínka pro Task 6 (rekapitulace s odrážkami). Samostatná úloha, protože jde o **sdílenou komponentu s pěti volajícími** a zaslouží si vlastní ověření.

**Files:**
- Modify: `web/src/components/ConfirmDialog.tsx:1-2, 7, 64`

**Interfaces:**
- Produces: `ConfirmDialogProps.message: ReactNode` (dosud `string`)

- [ ] **Step 1: Ověř, že změna nikoho nerozbije (red-green naruby)**

Run: `cd web && npx tsc -b`
Expected: PASS (výchozí stav).

Pak si vypiš všech pět volajících, abys po změně věděl, co ověřit:

Run: `cd web && npx grep -rn "ConfirmDialog" src --include=*.tsx` (nebo `Select-String`)
Expected: `BillingPage.tsx:717`, `InvoicesSection.tsx:375`, `DocumentsPage.tsx:350`, `UsersPage.tsx:307`, `SaldoPage.tsx:127` — všech pět předává `message` jako řetězec.

- [ ] **Step 2: Doplň typový import**

Pod řádek 2 (`import { AlertTriangle } from 'lucide-react';`) vlož:

```tsx
import type { ReactNode } from 'react';
```

Musí být `import type` — `verbatimModuleSyntax: true` jinak shodí `tsc -b`.

- [ ] **Step 3: Rozšiř typ propu**

Řádek 7, `  message: string;` → `  message: ReactNode;`

- [ ] **Step 4: Změň `<p>` na `<div>`**

Řádek 64 zní:

```tsx
            <p className="mt-2 text-sm text-text-secondary leading-relaxed">{message}</p>
```

Přepiš na:

```tsx
            <div className="mt-2 text-sm text-text-secondary leading-relaxed">{message}</div>
```

Třídy zůstávají. Bez této změny je rozšíření typu k ničemu — `<p>` nesmí obsahovat `<ul>`/`<div>` a prohlížeč by odstavec předčasně uzavřel.

- [ ] **Step 5: Ověř, že všech pět volajících dál prochází**

Run: `cd web && npx tsc -b && npm run lint && npm run build`
Expected: vše PASS. Rozšíření `string` → `ReactNode` je zpětně kompatibilní, žádný volající se nemění.

- [ ] **Step 6: Ruční kontrola**

Run: `cd web && npm run dev`
Otevři libovolný dialog (např. Uživatelé → smazat uživatele) a ověř, že text vypadá stejně jako předtím.

- [ ] **Step 7: Commit**

```bash
git add web/src/components/ConfirmDialog.tsx
git commit -m "refactor(web): ConfirmDialog message accepts ReactNode"
```

---

### Task 4: BillingPage — hlavička adminu a záhlaví tabulky náhledu

**Files:**
- Modify: `web/src/pages/BillingPage.tsx` — import ~ř. 13, hlavička ~ř. 104/112, `SettlementTable` ~ř. 858–880

- [ ] **Step 1: Přidej import**

Za řádek `import { InvoicesSection } from '../components/InvoicesSection';` vlož:

```tsx
import { HelpNote } from '../components/help/HelpNote';
import { HelpDisclosure } from '../components/help/HelpDisclosure';
import { HelpTerm } from '../components/help/HelpTerm';
```

- [ ] **Step 2: HelpNote do hlavičky adminu**

Najdi (uvnitř `AdminBillingView`, ~ř. 99–104):

```tsx
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Vyúčtování</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Správa zúčtovacích období a vyúčtování
          </p>
        </div>
```

Pozor: `<h1>…Vyúčtování</h1>` je v souboru **2×** — druhý výskyt je v `MemberBillingView`. Rozlišíš je podtitulkem „Správa zúčtovacích období".

Vlož `<HelpNote sectionId="billingAdmin" />` mezi `</p>` a `</div>`. Stávající `<p>` **nemaž**.

- [ ] **Step 3: HelpDisclosure pod hlavičku**

Za `</div>` uzavírající hlavičkový flex-řádek (~ř. 111), nad komentář `{/* Create form */}`, vlož:

```tsx
      <HelpDisclosure sectionId="billingAdmin" />
```

**Nedávej ho dovnitř hlavičkového divu** — ten je `flex items-center justify-between` s tlačítkem vpravo a rozbalovací `<details>` by rozhodil zarovnání.

- [ ] **Step 4: Sedm HelpTerm do SettlementTable**

Funkce `SettlementTable` začíná ~ř. 829. Do každého `<th>` vlož `HelpTerm` **za text**:

```tsx
              Ztráta m³ <HelpTerm id="ztrata" />
              Podíl % <HelpTerm id="podil" />
              Částka Kč <HelpTerm id="castka" />
              Zálohy Kč <HelpTerm id="zalohy" />
              Výsledek Kč <HelpTerm id="vysledek" />
              Z fondu Kč <HelpTerm id="zFondu" />
              Finální saldo Kč <HelpTerm id="finalniSaldo" />
```

**Kritické rozlišení:** v souboru jsou dvě tabulky s **znak po znaku shodným** markupem `<th>`. `SettlementTable` (~829–974) je náhled před uzávěrkou a má sloupce „Z fondu Kč" i „Finální saldo Kč". `ClosedSettlementTable` (~978–1068) je tabulka uzavřeného období, končí sloupcem „PDF" a **nápovědy nedostává**. Spolehlivý rozlišovač: pouze v `SettlementTable` následuje po „Výsledek Kč" hlavička „Z fondu Kč".

Sloupec „Spotřeba m³" nápovědu nedostává — pojem pro něj není definovaný (spec §13).

- [ ] **Step 5: Ověř**

Run: `cd web && npx tsc -b && npm run lint && npm run build`
Expected: vše PASS. Překlep v `id` by spadl už na `tsc -b`.

- [ ] **Step 6: Ruční kontrola**

Run: `cd web && npm run dev`
Přihlas se jako admin → Vyúčtování. Ověř: pod nadpisem je nová věta, pod ní rozbalovací „Jak se počítá vyúčtování". Rozbal období → Vypočítat vyúčtování → v záhlaví tabulky je sedm `(?)`. Klikni na jeden a ověř, že popover **není verzálkami** a nepřetéká mimo obrazovku na pravém okraji.

- [ ] **Step 7: Commit**

```bash
git add web/src/pages/BillingPage.tsx
git commit -m "feat(web): help for settlement calculation and preview table columns"
```

---

### Task 5: BillingPage — bloky fondu, ceny vody a metody rozdělení ztrát

**Files:**
- Modify: `web/src/pages/BillingPage.tsx` — blok ① ~ř. 645, blok ② ~ř. 669, výběr metody ~ř. 578–596

- [ ] **Step 1: Nápověda do bloku ①**

Najdi:

```tsx
              <h3 className="text-sm font-semibold">① Čerpání ze společného fondu</h3>
```

Hned pod `</h3>` vlož:

```tsx
              <HelpNote sectionId="fundDraw" />
              <HelpDisclosure sectionId="fundDraw" />
```

Znak `①` je kroužkovaná číslice (U+2460) — při hledání ho zachovej přesně.

- [ ] **Step 2: Nápověda do bloku ②**

Najdi `<h3 className="text-sm font-semibold">② Cena vody pro příští zálohy</h3>` a pod něj vlož:

```tsx
              <HelpNote sectionId="waterPriceCarry" />
              <HelpDisclosure sectionId="waterPriceCarry" />
```

- [ ] **Step 3: Nápověda k metodě rozdělení ztrát**

Najdi blok s `<label htmlFor="method-select"` (~ř. 578–595). Za `</div>` uzavírající tento blok (~ř. 596) vlož:

```tsx
        <HelpDisclosure sectionId="lossMethod" />
```

Toto je **primární** umístění — tady se metoda volí pro konkrétní výpočet. Druhé (v `AdvancesPage`) řeší Task 9.

- [ ] **Step 4: Ověř**

Run: `cd web && npx tsc -b && npm run lint && npm run build`
Expected: vše PASS.

- [ ] **Step 5: Ruční kontrola — pozor na podmíněné renderování**

Run: `cd web && npm run dev`

Blok ① se renderuje **jen** při `fundBalance !== null && fundBalance > 0 && activeHouseCount`, blok ② **jen** při `effectiveWaterPrice !== null && currentWaterPrice`, a oba jsou uvnitř `{preview && …}`. Aby ses k nim vůbec dostal: otevřené období → Vypočítat vyúčtování → a ve fondu musí být kladný zůstatek. Když blok ① nevidíš, není to chyba implementace — zkontroluj Hospodaření → zůstatek fondu.

- [ ] **Step 6: Commit**

```bash
git add web/src/pages/BillingPage.tsx
git commit -m "feat(web): help for fund draw, water price carry-forward and loss method"
```

---

### Task 6: Rekapitulace nastavených hodnot v potvrzovacím dialogu uzávěrky

Uzavírá otevřený nález #46 z `docs/ANALYZA-ADRESARE.md`.

**Files:**
- Modify: `web/src/pages/BillingPage.tsx` ~ř. 714–722

**Interfaces:**
- Consumes: `ConfirmDialogProps.message: ReactNode` z Tasku 3

- [ ] **Step 1: Najdi dnešní podobu dialogu**

```tsx
      <ConfirmDialog
        isOpen={showCloseConfirm}
        title="Uzavřít zúčtovací období"
        message="Opravdu chcete uzavřít období? Tato akce je nevratná. Budou vygenerovány PDF vyúčtování pro všechny domácnosti."
        confirmLabel="Uzavřít období"
        confirmVariant="danger"
        onConfirm={() => void handleClose()}
        onCancel={() => setShowCloseConfirm(false)}
      />
```

- [ ] **Step 2: Nahraď prop `message` složeným uzlem**

```tsx
        message={
          <>
            <p>
              Uzavřením se zapíše vyúčtování pro {preview?.houses.length ?? 0} domácností
              a vygenerují se PDF. Období už nepůjde otevřít ani upravit.
            </p>
            {(fundDraw > 0 || (applyNewPrice && effectiveWaterPrice !== null)) && (
              <ul className="mt-2 list-disc space-y-1 pl-5">
                {fundDraw > 0 && (
                  <li>
                    Z fondu se čerpá <strong>{formatCZK(fundDraw)} Kč</strong> —{' '}
                    {formatCZK(perHouseFundCredit)} Kč na domácnost.
                  </li>
                )}
                {applyNewPrice && effectiveWaterPrice !== null && (
                  <li>
                    Cena vody se přepíše na <strong>{formatCZK(effectiveWaterPrice)} Kč/m³</strong>{' '}
                    s platností od {formatDate(newPriceValidFrom)}.
                  </li>
                )}
              </ul>
            )}
            <p className="mt-2">Tato akce je nevratná.</p>
          </>
        }
```

Tři věci, na kterých záleží:

1. **Počet domácností je `preview.houses.length`, ne `activeHouseCount`.** Jsou to různé množiny — `activeHouseCount` je dělitel čerpání fondu (aktivní domácnosti), zatímco vyúčtování se zapisuje pro domácnosti s vodoměrem a odečty. Přesně tento rozdíl popisuje nález #40. `preview` je `SettlementPreviewResponse | null`, proto `?.` a `?? 0`.
2. **Podmínka u ceny musí být doslova `applyNewPrice && effectiveWaterPrice !== null`** — tentýž výraz se posílá na API jako `applyNewWaterPrice` (~ř. 487). Rekapitulace musí číst tytéž proměnné, které jdou na server, ne vlastní kopii. Jinak by dialog sliboval něco jiného, než se provede.
3. `formatCZK` a `formatDate` jsou modulové funkce (~ř. 24–43) — dostupné bez importu.

- [ ] **Step 3: Ověř**

Run: `cd web && npx tsc -b && npm run lint && npm run build`
Expected: vše PASS. Kdyby `tsc -b` hlásil, že `message` nepřijímá element, Task 3 nebyl dokončen.

- [ ] **Step 4: Ruční kontrola všech čtyř kombinací**

Run: `cd web && npm run dev`

Otevřené období → Vypočítat vyúčtování → Uzavřít období. Ověř dialog pro:
- fond 0 Kč, cena nezaškrtnutá → jen dva odstavce, žádné odrážky
- fond > 0, cena nezaškrtnutá → jedna odrážka o fondu
- fond 0, cena zaškrtnutá → jedna odrážka o ceně
- obojí → dvě odrážky

Ve všech případech dialog **nesmí** být rozbitý (to by znamenalo, že Task 3 krok 4 neproběhl). Uzávěrku nedokončuj — je nevratná; ověřuj jen vzhled dialogu a zavři ho.

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/BillingPage.tsx
git commit -m "feat(web): recap fund draw and price change in period close dialog"
```

---

### Task 7: BillingPage — pohled člena a zmrazený snímek

**Files:**
- Modify: `web/src/pages/BillingPage.tsx` — `MemberBillingView` ~ř. 1091–1095, `ClosedPeriodDetail` ~ř. 804–811, `MemberSettlementDetail` ~ř. 1253–1260

- [ ] **Step 1: Hlavička pohledu člena**

V `MemberBillingView` (~ř. 1079) najdi druhý výskyt `<h1 …>Vyúčtování</h1>`, poznáš ho podle podtitulku „Přehled vašeho vyúčtování za uzavřená období". Pod podtitulek vlož:

```tsx
          <HelpNote sectionId="billingMember" />
          <HelpDisclosure sectionId="billingMember" />
```

- [ ] **Step 2: `closedSnapshot` v admin variantě**

`ClosedPeriodDetail` (~ř. 729) má tři brzké returny (loading ~782, error ~790, prázdno ~798). `HelpNote` patří až do **hlavního** returnu (~ř. 804), jinak se u načítání ani chyby nezobrazí. Vlož jako první prvek hlavního returnu:

```tsx
        <HelpNote sectionId="closedSnapshot" />
```

- [ ] **Step 3: `closedSnapshot` v member variantě**

Totéž v `MemberSettlementDetail` (~ř. 1186, brzké returny ~1201/1209/1220, hlavní return ~1253):

```tsx
        <HelpNote sectionId="closedSnapshot" />
```

Blok `<div className="space-y-4"> / {downloadError && (…)}` je v souboru **2×** a doslova shodný (~805–810 a ~1254–1259) — rozliš je podle obklopující funkce, ne podle úryvku.

- [ ] **Step 4: Ověř**

Run: `cd web && npx tsc -b && npm run lint && npm run build`
Expected: vše PASS.

- [ ] **Step 5: Ruční kontrola**

Run: `cd web && npm run dev`
Přihlas se jako **člen** (ne admin) → Vyúčtování. Ověř hlavičkovou nápovědu a po rozbalení uzavřeného období větu o zmrazeném snímku. Pak jako admin rozbal uzavřené období a ověř tutéž větu.

- [ ] **Step 6: Commit**

```bash
git add web/src/pages/BillingPage.tsx
git commit -m "feat(web): member-facing settlement help and closed-period snapshot note"
```

---

## Dávka B — Tier 2/3 a stránka Jak to funguje

### Task 8: SaldoPage

**Files:**
- Modify: `web/src/pages/SaldoPage.tsx` — hlavička ~ř. 92, `<th>` ~ř. 188–189, `PaymentForm` ~ř. 417, staré texty ~ř. 520–521

- [ ] **Step 1: Import**

```tsx
import { HelpNote } from '../components/help/HelpNote';
import { HelpDisclosure } from '../components/help/HelpDisclosure';
import { HelpTerm } from '../components/help/HelpTerm';
```

- [ ] **Step 2: `saldoLive` do hlavičky**

Pod stávající `<p>` „Každý dům má jeden čistý zůstatek…" (~ř. 89–92) vlož `<HelpNote sectionId="saldoLive" />` jako **samostatný** odstavec. Stávající větu **nemaž** — mluví o významu čísla a znaménkové konvenci, nový text o rozdílu proti vyúčtování. Jsou to dvě různé věci.

Hlavička je jediné místo na stránce bez podmínky — vidí ji všechny role včetně člena, což je pro tento text správně.

- [ ] **Step 3: Dva HelpTerm do záhlaví tabulky salda**

V `SaldoTable` (~ř. 155) najdi:

```tsx
              <th className="text-right px-3 py-3 border-l border-border">Úpravy</th>
              <th className="text-right px-4 py-3 bg-success-light border-l border-border font-bold">Čistý zůstatek</th>
```

Uprav na:

```tsx
              <th className="text-right px-3 py-3 border-l border-border">Úpravy <HelpTerm id="upravy" /></th>
              <th className="text-right px-4 py-3 bg-success-light border-l border-border font-bold">Čistý zůstatek <HelpTerm id="cistyZustatek" /></th>
```

Řetězec „Úpravy" je na stránce **3×** (ř. 188 měnit; ř. 254 „Úpravy (výplaty / počáteční stav):" a ř. 261 „Úpravy zůstatku" **neměnit**) a „Čistý zůstatek" **2×** (ř. 189 měnit, ř. 255 neměnit). Stránka má také dvě tabulky s `<thead>` — `SaldoTable` (~182–191) a `PaymentsList` (~538–550); patříš do té první.

- [ ] **Step 4: `paymentTypes` do formuláře**

V `PaymentForm` (~ř. 314) pod `<h2 className="text-lg font-semibold">Zaznamenat platbu / úpravu</h2>` (~ř. 417) vlož:

```tsx
      <HelpNote sectionId="paymentTypes" />
      <HelpDisclosure sectionId="paymentTypes" />
```

- [ ] **Step 5: Odstraň staré texty, které nový nahrazuje**

Smaž řádky ~520–521:

```tsx
      {kind === 'payout' && <p className="text-xs text-text-muted">Výplata vrací domu přeplatek — sníží jeho kredit (saldo jde k nule).</p>}
      {kind === 'opening' && <p className="text-xs text-text-muted">Jednorázové nastartování zůstatku domu při zavádění systému. Nevstupuje do vyúčtování.</p>}
```

Ověřeno, že se tyto věty nikde jinde v repozitáři nevyskytují — odstranění nic dalšího nerozbije.

- [ ] **Step 6: Ověř**

Run: `cd web && npx tsc -b && npm run lint && npm run build`
Expected: vše PASS.

- [ ] **Step 7: Ruční kontrola**

Run: `cd web && npm run dev`

Jako admin → Saldo a platby. Ověř větu v hlavičce, dva `(?)` v záhlaví tabulky a nápovědu nad lištou tlačítek typu platby. Zkontroluj, že odrážky v rozbalené nápovědě používají **stejné popisky jako tlačítka** („Měsíční záloha", „Výplata přeplatku") — jinak si je uživatel nespojí.

Pozor: `PaymentForm` se renderuje jen pro admina, a `SaldoTable` má brzký return při prázdných datech — při nulovém saldu `<th>` s nápovědami vůbec nevznikne.

- [ ] **Step 8: Commit**

```bash
git add web/src/pages/SaldoPage.tsx
git commit -m "feat(web): saldo page help for net balance, adjustments and payment types"
```

---

### Task 9: AdvancesPage — metoda rozdělení ztrát

**Files:**
- Modify: `web/src/pages/AdvancesPage.tsx` ~ř. 177–181

- [ ] **Step 1: Import a vložení k read-only dlaždici**

Najdi:

```tsx
              <p className="text-xs font-medium text-text-secondary uppercase">Rozdělení ztráty na síti</p>
              <p className="text-xl font-bold mt-1">{lossMethodLabel(settings?.lossAllocationMethod)}</p>
              <p className="text-xs text-text-muted mt-0.5">Používá se pro vyúčtování i saldo domácností.</p>
```

Pod poslední `<p>` vlož `<HelpDisclosure sectionId="lossMethod" />` a doplň import.

Toto je dlaždice v **režimu čtení** — to, co admin běžně vidí. Do `<select>` v editačním režimu (~ř. 229) nápověda nepatří; ternární operátor `{!editing ? (…) : form && (…)}` (~158/183) je vzájemně výlučný a editaci admin otevírá zřídka.

Řetězec „Rozdělení ztráty na síti" je na stránce **2×** (ř. 178 dlaždice, ř. 221 label u selectu) — měň jen ten první.

- [ ] **Step 2: Ověř**

Run: `cd web && npx tsc -b && npm run lint && npm run build`
Expected: vše PASS.

- [ ] **Step 3: Ruční kontrola**

Run: `cd web && npm run dev`
Jako admin → Hospodaření → Zálohy. Dlaždice „Rozdělení ztráty na síti" má pod sebou rozbalovací nápovědu s příkladem na 12 m³ a 8 domácnostech.

- [ ] **Step 4: Commit**

```bash
git add web/src/pages/AdvancesPage.tsx
git commit -m "feat(web): loss allocation method help on advances settings"
```

---

### Task 10: Zbylé stránky

Šest souborů, jedna až dvě kotvy každý. Drží pohromadě, protože žádná z nich nemá vlastní ověřovací cyklus, který by stál za samostatný commit.

**Files:**
- Modify: `web/src/pages/admin/HousesPage.tsx:202-205`
- Modify: `web/src/pages/admin/UsersPage.tsx:175`
- Modify: `web/src/pages/ReadingsImportPage.tsx:42, 394`
- Modify: `web/src/pages/DocumentsPage.tsx:136`
- Modify: `web/src/pages/InvoicesOverviewPage.tsx:113`
- Modify: `web/src/components/InvoicesSection.tsx:186`

- [ ] **Step 1: HousesPage — nahraď zavádějící `title`**

Dnešní stav (~ř. 202–205):

```tsx
                      <label className="mt-1.5 flex items-center gap-1.5 text-xs text-text-secondary" title="Dům neplatí pravidelně, přeplatek se postupně rozpouští">
                        <input type="checkbox" checked={editForm.dissolveOverpayment} onChange={(e) => setEditForm({ ...editForm, dissolveOverpayment: e.target.checked })} />
                        Rozpouští přeplatek
                      </label>
```

Odstraň atribut `title="…"` a za `</label>` vlož `<HelpTerm id="rozpoustiPreplatek" />`.

Dvě věci: stávající `title` **odstraň**, ne ponech — formulace „přeplatek se postupně rozpouští" svádí k představě, že se něco děje automaticky, zatímco příznak nic nepočítá (`House.cs:15`). A `HelpTerm` musí být **za** `</label>`, ne uvnitř — uvnitř by kliknutí na `(?)` přepnulo zaškrtávátko.

- [ ] **Step 2: UsersPage — nápověda k roli**

Za `<label className="block text-sm font-medium text-text-secondary mb-1">Role</label>` (~ř. 175) vlož `<HelpTerm id="role" />` (dovnitř labelu, za text). Select nabízí `Member` → „Člen", `Accountant` → „Účetní", `Admin` → „Admin" — text `role.long` používá tytéž popisky, nic se nepřejmenovává.

- [ ] **Step 3: ReadingsImportPage — dvoukrokovost a varování**

Do větve `{(tab === 'file' || tab === 'clipboard') && (` (~ř. 394), jako první prvek vnitřního `<div>`, vlož `<HelpNote sectionId="importTwoStep" />`.

**Nedávej to k podtitulku na ~ř. 361** — ten je nad záložkami a platí i pro „Ruční zadání", které ukládá okamžitě. Text o dvou krocích by tam lhal.

Do nadpisu bloku varování (~ř. 42) vlož oba pojmy:

```tsx
          <h4 className="text-sm font-semibold text-warning">Upozornění ({warnings.length}) <HelpTerm id="chybiOdecet" /> <HelpTerm id="anomalie" /></h4>
```

Souhrnné umístění je záměr: `ImportValidationMessage.Type` nese jen závažnost (`"warning"`/`"error"`), druh varování je zakódovaný pouze v české větě. Per-varování `HelpTerm` by vyžadoval porovnávání prefixů textu, což je křehké.

- [ ] **Step 4: DocumentsPage — verzování**

Za `</div>` uzavírající hlavičkový flex-row (~ř. 136) vlož `<HelpNote sectionId="documentVersions" />`. Pod `<h1>` dnes žádný podtitulek není, vzniká nové místo. Jeden `HelpNote` pod nadpisem pokrývá všechna tři místa, kde se verze zobrazují.

- [ ] **Step 5: InvoicesOverviewPage — nahraď větu komponentou**

Stávající `<p className="mt-1 text-sm text-text-secondary">Všechny přijaté faktury — …</p>` (~ř. 113–115) **nahraď** celý:

```tsx
        <HelpNote sectionId="receivedInvoicesFund" />
```

Text sekce už obsahuje obě původní věty plus třetí o kategorii „fond-voda". Nahradit, ne doplnit — kdyby původní `<p>` zůstal, existoval by tentýž text na dvou místech a porušil by pravidlo jednoho zdroje, kvůli kterému celý modul vznikl.

- [ ] **Step 6: InvoicesSection — disclosure pod stávající větu**

Stávající `<p className="text-xs text-text-muted mt-0.5">Jedna faktura = …</p>` (~ř. 186) **ponech** a pod něj vlož `<HelpDisclosure sectionId="invoiceLineItems" />`.

Původní návrh počítal s nahrazením, ale tím by zmizela jediná vždy viditelná věta v sekci. Proto zůstává jako `note` a disclosure přibývá pod ní (spec §13).

Import je zde `'./help/HelpDisclosure'` — soubor už je v `components/`.

- [ ] **Step 7: Ověř**

Run: `cd web && npx tsc -b && npm run lint && npm run build`
Expected: vše PASS.

- [ ] **Step 8: Ruční kontrola**

Run: `cd web && npm run dev`

Projdi jako admin: Domácnosti (edituj řádek → `(?)` u „Rozpouští přeplatek", starý tooltip je pryč), Uživatelé (nový uživatel → `(?)` u Role), Odečty → Import (věta o dvou krocích jen na záložkách Soubor a Odečítačka, ne na Ruční zadání; nahraj soubor s varováním a zkontroluj oba `(?)` u „Upozornění"), Dokumenty, Přehled faktur, Vyúčtování → Faktury za vodu.

- [ ] **Step 9: Commit**

```bash
git add web/src/pages/admin/HousesPage.tsx web/src/pages/admin/UsersPage.tsx web/src/pages/ReadingsImportPage.tsx web/src/pages/DocumentsPage.tsx web/src/pages/InvoicesOverviewPage.tsx web/src/components/InvoicesSection.tsx
git commit -m "feat(web): contextual help across houses, users, import, documents and invoices"
```

---

### Task 11: Stránka „Jak to funguje"

**Files:**
- Modify: `web/src/content/help.ts` — naplnit `guides`
- Create: `web/src/pages/JakToFungujePage.tsx`
- Modify: `web/src/App.tsx:21, 68`
- Modify: `web/src/components/Layout.tsx:17, 59`

**Interfaces:**
- Consumes: `guides`, `terms`, `sections` z `content/help.ts`

- [ ] **Step 1: Naplň `guides`**

Devět sekcí podle spec §9. Texty ber ze specifikace; sekce „Vyúčtování krok za krokem", „Zálohy, doplatky a saldo", „Společný fond" a „Odečty a import" skládej z už napsaných `sections`/`terms` — neopisuj je podruhé:

```ts
export const guides: Guide[] = [
  {
    id: 'co-aplikace-dela',
    title: 'Co aplikace dělá',
    body: [ /* odstavce ze spec §9, řádek „Co aplikace dělá" */ ],
  },
  {
    id: 'rocni-cyklus',
    title: 'Roční cyklus',
    body: [ /* pět kroků ze spec §9 */ ],
  },
  {
    id: 'vyuctovani',
    title: 'Vyúčtování krok za krokem',
    body: [sections.billingAdmin.disclosure!, sections.lossMethod.disclosure!],
  },
  {
    id: 'zalohy-a-saldo',
    title: 'Zálohy, doplatky a saldo',
    body: [sections.paymentTypes.disclosure!, terms.cistyZustatek.long!],
  },
  {
    id: 'fond',
    title: 'Společný fond',
    body: [sections.fundDraw.disclosure!, sections.waterPriceCarry.disclosure!],
  },
  {
    id: 'odecty',
    title: 'Odečty a import',
    body: [sections.importTwoStep.note!, terms.chybiOdecet.long!, terms.anomalie.long!],
  },
  {
    id: 'role',
    title: 'Role a přístup',
    body: [terms.role.long!],
  },
  {
    id: 'prihlaseni',
    title: 'Přihlášení',
    body: ['Do portálu se přihlásíte odkazem zaslaným e-mailem. Odkaz platí 15 minut a lze ho použít jen jednou. Pokud vám nedorazil, zkontrolujte složku s nevyžádanou poštou a zkuste odkaz vyžádat znovu.'],
  },
];
```

Křížové odkazy přes `sections.X.disclosure!` jsou to, co drží stránku a kontextové nápovědy synchronizované — proto zákaz kopírovat text podruhé. Non-null assertion `!` je bezpečný, protože klíče jsou typované a hodnoty vyplněné v Tasku 1.

- [ ] **Step 2: Stránka**

```tsx
import { guides, terms } from '../content/help';
import type { TermId } from '../content/help';

export function JakToFungujePage() {
  const termIds = Object.keys(terms) as TermId[];

  return (
    <div className="space-y-8">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Jak to funguje</h1>
        <p className="mt-1 text-sm text-text-secondary">
          Výklad toho, co aplikace dělá a jak počítá čísla, která vidíte na obrazovkách.
        </p>
      </div>

      <nav className="rounded-2xl border border-border bg-surface-sunken p-4">
        <ul className="space-y-1 text-sm">
          {guides.map((g) => (
            <li key={g.id}>
              <a href={`#${g.id}`} className="text-accent hover:underline">{g.title}</a>
            </li>
          ))}
          <li><a href="#slovnik" className="text-accent hover:underline">Slovník pojmů</a></li>
        </ul>
      </nav>

      {guides.map((g) => (
        <section key={g.id} id={g.id} className="scroll-mt-8">
          <h2 className="text-lg font-semibold text-text-primary">{g.title}</h2>
          <div className="mt-2 space-y-2 text-sm text-text-secondary">
            {g.body.flatMap((para, i) =>
              para.split('\n\n').map((p, j) => <p key={`${i}-${j}`}>{p}</p>),
            )}
          </div>
        </section>
      ))}

      <section id="slovnik" className="scroll-mt-8">
        <h2 className="text-lg font-semibold text-text-primary">Slovník pojmů</h2>
        <dl className="mt-2 space-y-3">
          {termIds.map((id) => (
            <div key={id} id={id} className="scroll-mt-8">
              <dt className="text-sm font-semibold text-text-primary">{terms[id].label}</dt>
              <dd className="text-sm text-text-secondary">
                {terms[id].long ?? terms[id].short}
              </dd>
            </div>
          ))}
        </dl>
      </section>
    </div>
  );
}
```

`id={id}` u položek slovníku je cíl odkazu „Více" z `HelpTerm` — bez něj vede odkaz na stránku, ale ne na pojem. `scroll-mt-8` zajistí, že se kotva neschová pod horní okraj.

- [ ] **Step 3: Route**

V `App.tsx` pod poslední page import (`import { InvoicesOverviewPage } from './pages/InvoicesOverviewPage';`, ~ř. 21) vlož:

```tsx
import { JakToFungujePage } from './pages/JakToFungujePage';
```

A mezi `<Route path="/finance" …>` (~ř. 68) a `<Route` uvozující `/admin/houses` (~ř. 69) vlož s odsazením 14 mezer:

```tsx
              <Route path="/jak-to-funguje" element={<JakToFungujePage />} />
```

**Bez `ProtectedRoute` obalu** — stránka je pro všechny role. Přihlášení je stejně zajištěné vnějším `<Route element={<ProtectedRoute><Layout /></ProtectedRoute>}>` (~ř. 31–93); route **musí zůstat uvnitř něj**, jinak se vykreslí bez sidebaru.

Použij **eager** import, ne `lazy()`. V projektu dnes neexistuje žádný `Suspense` boundary a `lazy()` by bez něj za běhu spadl. K `lazy()` sáhni jen tehdy, kdyby `npm run build` po přidání stránky hlásil nový problém s velikostí — pak obal `<Outlet />` v `Layout.tsx:236`.

- [ ] **Step 4: Navigace**

V `Layout.tsx` mezi `  Scale,` (~ř. 17) a `  LogOut,` (~ř. 18) vlož `  CircleHelp,`.

Pak mezi `  },` uzavírající skupinu Odečty (~ř. 59) a `];` uzavírající `navItems` (~ř. 60) vlož:

```tsx
  { label: 'Jak to funguje', path: '/jak-to-funguje', icon: <CircleHelp size={iconSize} /> },
```

Dvě pasti: vložení **za** `];` rozbije soubor; a položka **nesmí** mít `adminOnly` ani `financeManager`, jinak ji filtr na ~ř. 111 členům skryje. Umístění „před oddělovačem administrace" vyjde samo — oddělovač se renderuje až za celým `navItems.map()`.

- [ ] **Step 5: Ověř**

Run: `cd web && npx tsc -b && npm run lint && npm run build`
Expected: vše PASS. Kdyby `tsc -b` hlásil nepoužitou proměnnou, zapomněl jsi ikonu použít (`noUnusedLocals`).

- [ ] **Step 6: Ruční kontrola včetně křížových odkazů**

Run: `cd web && npm run dev`

1. V sidebaru je „Jak to funguje" **jako člen i jako admin**, mezi Odečty a Administrací.
2. Stránka se otevře se sidebarem (ne holá).
3. Odkazy v obsahu skáčou na sekce.
4. **Klíčová zkouška synchronizace:** jdi na Vyúčtování → rozbal „Jak se počítá vyúčtování" a porovnej text s toutéž sekcí na stránce Jak to funguje. Musí být **doslova shodný** — pokud ne, Task 11 krok 1 text zkopíroval místo odkazu.
5. Klikni na `(?)` u sloupce „Ztráta m³" → „Více" → musí skočit na `#ztrata` ve slovníku.

- [ ] **Step 7: Commit**

```bash
git add web/src/content/help.ts web/src/pages/JakToFungujePage.tsx web/src/App.tsx web/src/components/Layout.tsx
git commit -m "feat(web): 'Jak to funguje' page with guides and generated glossary"
```

---

## Poznámky k ověřování

**Proč tu nejsou jednotkové testy.** `web/package.json` nemá test runner ani žádnou testovací závislost, a spec (§10) zakládání runneru vědomě vylučuje. Psát sem vitest testy by znamenalo, že první `npm test` skončí na „command not found". Náhradou je `tsc -b` jako typová pojistka (Task 1 krok 4 ověřuje, že skutečně funguje), `npm run lint` jako CI gate a explicitní ruční průchod v každé úloze.

**Co typová kontrola nechytí:** sirotčí termín — definovaný v `terms`, nikde nepoužitý. Po dokončení celého plánu se to dá ověřit jednorázově:

```bash
cd web/src && for id in ztrata podil castka zalohy vysledek zFondu finalniSaldo upravy cistyZustatek rozpoustiPreplatek role chybiOdecet anomalie; do
  grep -rq "id=\"$id\"" --include=*.tsx . || echo "nepoužitý pojem: $id"
done
```

Očekávaný výstup: prázdný. Pojmy `chybiOdecet` a `anomalie` se používají jen v Tasku 10 krok 3 — pokud se ohlásí, ten krok neproběhl.
