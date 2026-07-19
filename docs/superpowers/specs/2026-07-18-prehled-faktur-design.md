# Přehled přijatých faktur — návrh

**Datum:** 2026-07-18
**Autor:** brainstorming session (Rosťa Čendelín + Claude)
**Stav:** schváleno k implementaci

## 1. Kontext

Portál dnes eviduje „co jsme zaplatili / na co nám přišla faktura" ve **dvou oddělených entitách**:

- **`SupplierInvoice`** — výhradně **faktury za vodu**. Strukturovaná: hlavička (číslo, DPH, vystaveno, splatnost) + N dílčích řádků (období od–do, počáteční/koncový stav, spotřeba, Kč/m³, cena). Má plnou podporu přílohy (upload/download PDF do blob kontejneru `invoices`). Vstupuje do vyúčtování vody **po řádcích** (per-line-item `DateFrom`).
- **`FinancialRecord`** — obecné finanční záznamy (kategorie voda/elektro/udrzba/pojisteni/jine, typ Příjem/Výdaj). Plochý záznam. Pole `AttachmentBlobName` v modelu **existuje, ale nemá žádný upload/download endpoint ani UI** — v praxi je vždy bez přílohy.

Neexistuje žádné místo, kde by šlo zobrazit **všechny přijaté faktury pohromadě** (voda i ostatní). Navíc byl při QA testování odhalen bug: faktura za vodu, ze které vychází vyúčtování, se nezobrazí v seznamu „Faktury za vodu" pro žádný rok (viz sekce 4).

## 2. Cíl

1. **Jednotný přehled všech přijatých faktur** (voda + ostatní výdaje) na jedné stránce, s filtrem podle roku a kategorie.
2. **Oprava bugu** s nedohledatelnými fakturami — sjednocení definice „faktura patří do roku X".
3. **Doplnění příloh k `FinancialRecord`** — aby šlo přiložit PDF i k nevodním fakturám (elektro, pojištění…).

## 3. Rozsah

**V rozsahu:**
- Nový read-only endpoint `GET /invoices/all?year=&category=` spojující obě entity.
- Nová stránka „Přehled faktur" pod sekcí Hospodaření.
- Upload/download přílohy k `FinancialRecord` (backend endpointy + UI ve FinancePage).
- Oprava filtrování podle roku (nový přehled i stávající seznam „Faktury za vodu").

**Mimo rozsah:**
- Editace faktur/záznamů z nové stránky (zůstává na Vyúčtování a Hospodaření).
- Zahrnutí `FinancialRecord` typu **Příjem** (přehled je čistě o *přijatých fakturách* = výdajích).
- Jakákoli změna výpočtu vyúčtování vody.

## 4. Oprava bugu: jednotná definice „patří do roku"

### Současný stav (tři nekonzistentní definice)

| Kde | Podle čeho |
|---|---|
| `GET /invoices?year=` (seznam faktur za vodu) | přesná shoda `invoice.Year`, kde `Year` = rok **nejstaršího řádku** napříč celou fakturou |
| `BillingPeriod.totalInvoiceAmount` | `(Year, Month)` faktury v rozsahu období, sčítá celou `Amount` |
| Výpočet vyúčtování | **každý řádek zvlášť** — započítá se, jen když řádkové `DateFrom` padne do rozsahu období |

**Důsledek:** faktura s nejstarším řádkem v roce 2022 (a dalšími řádky 2024–2026) má `invoice.Year = 2022`. Dropdown „Faktury za vodu" ale nabízí jen natvrdo roky `currentYear+1 … currentYear-3` (2023–2027), takže faktura je v UI **nedohledatelná pro každý rok**, přestože její pozdější řádky do vyúčtování korektně vstupují.

### Cílový stav

Pro **zobrazování/procházení** (nový přehled i stávající seznam „Faktury za vodu") se používá jednotná, intuitivní definice: **rok data dokumentu**.

- `SupplierInvoice` → `IssuedDate.Year` (kdy byla faktura vystavena/přijata).
- `FinancialRecord` → `Date.Year`.

Roky nabízené ve filtrech se **odvodí ze skutečných dat** (rozsah `min…max` roků reálných dokumentů), ne z pevného okna kolem aktuálního roku.

**Výpočet vyúčtování se nemění.** Ten běží po řádcích (`per-line-item DateFrom`) nezávisle na tomto zobrazovacím filtru — je to samostatná code-path a tato změna se ho nedotkne. `BillingPeriod.totalInvoiceAmount` (dopočet u seznamu období) také ponecháváme beze změny; není součástí tohoto bugu ani přehledu.

## 5. Datový model & API

### 5.1 Nový endpoint

```
GET /invoices/all?year=<int>&category=<string?>
Auth: Admin, Accountant
```

Vrací seznam sjednocených DTO (řazeno podle `date` sestupně):

```csharp
public record ReceivedInvoiceResponse(
    string Source,        // "voda" | "ostatni"
    string Id,            // entity Id (SupplierInvoice.Id / FinancialRecord.Id)
    DateTime Date,        // IssuedDate (voda) | Date (ostatni)
    string Category,      // "voda" | FinancialRecord.Category
    string Description,   // InvoiceNumber (voda) | Description (ostatni)
    decimal Amount,       // celkem vč. DPH (voda) | Amount (ostatni)
    DateTime? DueDate,    // faktury za vodu mají splatnost; záznamy null
    bool CountsTowardWaterSettlement, // true pro Source="voda"
    bool HasAttachment,
    string? AttachmentDownloadPath    // relativní API cesta ke stažení, nebo null
);
```

- `Source="voda"` položky pocházejí ze `SupplierInvoice`, filtrované podle `IssuedDate.Year == year`. `CountsTowardWaterSettlement = true`. `AttachmentDownloadPath = /invoices/{id}/attachment` (pokud `AttachmentBlobName != null`).
- `Source="ostatni"` položky pocházejí z `FinancialRecord` typu **Expense**, filtrované podle `Date.Year == year` (a volitelně `Category`). `CountsTowardWaterSettlement = false`. `AttachmentDownloadPath = /finance/{id}/attachment` (pokud `AttachmentBlobName != null`).
- Filtr `category`: `"voda"` → jen SupplierInvoice; jiná hodnota → jen FinancialRecord dané kategorie; prázdný → obojí.
- Nový use case `GetReceivedInvoicesUseCase` (aplikační vrstva), po vzoru `GetFundBalanceUseCase`. Čistě READ, žádný zápis.

### 5.2 Přílohy k FinancialRecord

Mirror existujícího `InvoiceFunctions.UploadInvoiceAttachment` / `DownloadInvoiceAttachment`:

```
POST /finance/{id}/attachment   Auth: Admin       — raw body, Content-Type header; uloží do blob "finance/{id}/{filename}", nastaví FinancialRecord.AttachmentBlobName
GET  /finance/{id}/attachment   Auth: Admin, Accountant — vrátí soubor (stejný způsob jako invoice download)
```

- Blob kontejner `finance` (konstanta v `BlobContainerNames`, cesta `finance/{recordId}/{filename}` — už uvedeno v CLAUDE.md).
- Validace typu/velikosti souboru stejná jako u invoice/document uploadu (20 MB, povolené content-typy).

### 5.3 Odvození roků z dat

Data jsou droboučká (8 domácností, jednotky faktur ročně), proto volíme nejjednodušší mechanismus **bez samostatného endpointu na roky**:

- `year` je na endpointu **volitelný** parametr. Bez něj endpoint vrátí **všechny** přijaté faktury napříč roky.
- Nová stránka při načtení zavolá endpoint **bez `year`**, z vrácených dat si odvodí seznam dostupných roků (unikátní `date.getFullYear()`) a filtrování podle roku i kategorie provede **v paměti prohlížeče**. Serverové parametry `year`/`category` tak zůstávají k dispozici pro přímé API volání, ale stránka je nepotřebuje pro každý překlik filtru.

## 6. UI/UX

### Nová stránka „Přehled faktur"

- **Route:** `/prehled-faktur`, guard **Admin + Účetní** (finanční data; Člen nemá vidět cizí faktury).
- **Navigace:** nová položka v Sidebaru v sekci Hospodaření (vedle Zálohy / Saldo / Vyúčtování).
- **Filtry:** Rok (z dat), Kategorie (Vše / Voda / Elektro / Údržba / Pojištění / Jiné).
- **Souhrn:** celková částka za aktuální filtr, rozdělená na „voda" vs. „ostatní".
- **Tabulka:** Datum · Kategorie (badge) · Popis/Číslo · Splatnost · Zdroj (badge „voda" = vstupuje do vyúčtování) · Částka · Příloha (Stáhnout / —).
- **Read-only browse.** Editace není součástí této stránky.

### FinancePage — přílohy

- Formulář „Nový záznam" / editace dostane **volitelný upload souboru** (PDF/obrázek).
- V tabulce záznamů přibude sloupec/ikona **Příloha** s odkazem na stažení.

### Stávající seznam „Faktury za vodu" (InvoicesSection)

- Dropdown roků se generuje **z reálných dat** (ne pevné okno).
- Filtr faktur se řídí `IssuedDate.Year` (aby faktura byla dohledatelná pod rokem vystavení).

## 7. Chybové stavy

- `GET /invoices/all` bez `year` → vrátí všechny roky (nebo dle mechanismu v plánu). Prázdný výsledek = prázdná tabulka s hláškou „Za rok {rok} zatím nejsou žádné faktury.".
- Upload přílohy: překročení velikosti / nepovolený typ → česká chybová hláška (stejný vzor jako invoice/document upload).
- Download neexistující přílohy / záznamu → 404 s českou hláškou.
- Člen/nepřihlášený na `/prehled-faktur` nebo na endpoint → RBAC blok (403).

## 8. Testování

- **Backend unit testy** (`Oaza.Application.Tests`):
  - `GetReceivedInvoicesUseCase`: spojení obou zdrojů, filtr roku podle `IssuedDate`/`Date`, filtr kategorie, `CountsTowardWaterSettlement`/`Source`/`HasAttachment` mapování, řazení podle data.
  - Filtr `category="voda"` vrací jen SupplierInvoice; jiná kategorie jen FinancialRecord; prázdný obojí.
- **Attachment endpointy:** ověření nastavení `AttachmentBlobName` a RBAC (dle existujícího vzoru; Functions vrstva nemá dnes HTTP testy, takže logika pokrytá na úrovni use case / mapperu, kde to dává smysl).
- **Frontend:** manuálně (projekt nemá frontend test framework) — proklik nové stránky, filtrů, stažení přílohy, upload na FinancePage.

## 9. Dopad na CLAUDE.md pravidla

- **„BillingPeriod total = SUM(SupplierInvoice.Amount), nikdy FinancialRecord"** zůstává **strukturálně vynucené**: přehled je čistě READ view, entity zůstávají oddělené, do výpočtu vyúčtování se nic nemíchá.
- Do „Implementation additions" v CLAUDE.md se doplní odstavec o novém přehledu, endpointu, attachmentech FinancialRecordu a sjednocené definici roku pro zobrazování faktur.
