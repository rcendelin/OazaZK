# REST API — reference

> Vygenerováno z kódu (`api/src/Oaza.Functions/Endpoints/*.cs`), stav větve `develop`, září 2026. Při přidání nebo změně endpointu aktualizuj tuto tabulku.

- **Base URL:** `https://func-oaza-{dev|test|prod}.azurewebsites.net/api` (lokálně `http://localhost:7071/api`).
- **Formát:** JSON, camelCase. Enumy jako řetězce (`"Admin"`, `"Equal"`, …). Data ISO 8601.
- **Autentizace:** `Authorization: Bearer <token>` — vlastní JWT z magic linku nebo Entra ID token. Viz [ARCHITEKTURA.md](ARCHITEKTURA.md#4-autentizace).

Sloupec **Přístup**:

- **veřejné** — `[AllowAnonymous]`,
- **přihlášený** — libovolná role,
- **Admin**, **Admin, Účetní** — `[RequireRole]`.

Všechny funkce mají `AuthorizationLevel.Anonymous`; přístup vynucuje middleware, nikoli function keys.

---

## Autentizace — `AuthFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| POST | `/auth/magic-link` | veřejné | `{ email }` — pošle odkaz pro přihlášení. Vždy 200. Max 3 žádosti/h. |
| POST | `/auth/magic-link/verify` | veřejné | `{ token, email }` → `{ token, expiresAt }` (JWT 24 h). 401 při neplatném/vypršelém odkazu. |
| GET | `/auth/me` | přihlášený | Profil aktuálního uživatele. |

## Domácnosti — `HouseFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/houses` | přihlášený | Všechny domy (vč. kontaktů). |
| GET | `/houses/{id}` | přihlášený | Detail domu. |
| POST | `/houses` | Admin | `{ name, address, contactPerson, email }` |
| PUT | `/houses/{id}` | Admin | + `isActive`, `dissolveOverpayment` |

## Vodoměry — `MeterFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/meters` | přihlášený | Všechny vodoměry. |
| POST | `/meters` | Admin | `{ meterNumber, name, type: Main\|Individual, houseId?, radioAddress? }` |
| PUT | `/meters/{id}` | Admin | `{ meterNumber, name, houseId?, radioAddress? }` (typ nelze měnit) |

## Uživatelé — `UserFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/users` | Admin | Všichni uživatelé. |
| POST | `/users` | Admin | `{ name, email, role, houseId?, authMethod: EntraId\|MagicLink }` — pošle pozvánku (selhání e-mailu nebrání vytvoření). |
| PUT | `/users/{id}` | Admin | `{ name, role?, houseId?, notificationsEnabled? }` |
| DELETE | `/users/{id}` | Admin | Smazání uživatele (ne sebe sama). |

## Odečty — `ReadingFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/readings?year=&month=` | přihlášený | Odečty za měsíc (oba parametry povinné). Admin vše, ostatní jen vodoměry vlastního domu. |
| GET | `/readings/all` | Admin | Všechny odečty všech vodoměrů. |
| GET | `/readings/chart?houseId=&from=&to=` | přihlášený | Měsíční spotřeba pro graf (výchozí 12 měsíců). Člen jen vlastní dům; Admin bez `houseId` = součet domů. |
| GET | `/readings/estimate?meterId=&date=` | Admin | Odhad stavu vodoměru k datu (T04): skutečný odečet, interpolace po dnech, nebo nejbližší odečet, když existuje jen jedna strana ([VYUCTOVANI §7.5](VYUCTOVANI.md#75-odhad-odečtu-t04)). Nic neukládá. |
| POST | `/readings` | Admin | `{ meterId, readingDate, value, isEstimate?, estimateNote? }` — ruční odečet; odhad vyžaduje `estimateNote`. |
| PUT | `/readings/{meterId}/{yyyy-MM-dd}` | Admin | `{ value, newDate? }` — oprava hodnoty/přesun data. 409 v uzavřeném období. |
| POST | `/readings/import` | Admin | Excel (multipart nebo raw body, max 5 MB) → náhled. Nic neukládá. |
| POST | `/readings/import/clipboard` | Admin | `{ text, readingDate }` — tabulátorový text s hlavičkou `Address`, `Value 1` → náhled. |
| POST | `/readings/import/confirm` | Admin | `{ readings: [{ meterId, readingDate, value }] }` — odečty z náhledu; server je znovu ověří a uloží všechny, nebo žádný ([VYUCTOVANI §7.4](VYUCTOVANI.md#74-potvrzení)). 400 chyba validace, 409 odečet přibyl od náhledu. |

Formát souborů a validační pravidla: [VYUCTOVANI.md §7](VYUCTOVANI.md#7-import-odečtů).

## Faktury dodavatele vody — `InvoiceFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/invoices?year=` | Admin, Účetní | Faktury za vodu (rok podle `IssuedDate`). |
| GET | `/invoices/all?year=&category=` | Admin, Účetní | Sjednocený přehled přijatých faktur: faktury za vodu + výdaje z hospodaření. Jen čtení. |
| POST | `/invoices` | Admin | `{ invoiceNumber, issuedDate, dueDate, vatRatePercent, lineItems: [{ dateFrom, dateTo, startReading, endReading, consumptionM3, unitPrice, amountExclVat }] }` |
| PUT | `/invoices/{id}` | Admin | Stejné tělo. 409 v uzavřeném období. |
| DELETE | `/invoices/{id}` | Admin | 409 v uzavřeném období. |
| POST | `/invoices/{id}/attachment` | Admin | PDF příloha (max 20 MB). |
| GET | `/invoices/{id}/attachment` | Admin, Účetní | Stažení PDF. |

## Zálohy, platby a saldo — `AdvanceFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/advances?houseId=&year=` | přihlášený | Platby. Člen jen vlastní dům (cizí `houseId` → 403). |
| GET | `/advances/saldo?houseId=` | přihlášený | Saldo domů ([VYUCTOVANI.md §5](VYUCTOVANI.md#5-saldo-domu)). Člen jen vlastní dům. |
| POST | `/advances` | Admin | Měsíční záloha `{ houseId, year, month, waterAmount, electricityAmount, commonAmount, paymentDate }`. 409 při duplicitě. |
| PUT | `/advances/{houseId}/{yyyy-MM}` | Admin | `{ waterAmount, electricityAmount, commonAmount, paymentDate }`. 409 v uzavřeném období. |
| POST | `/advances/doplatek` | Admin | `{ houseId, waterAmount, electricityAmount, commonAmount, paymentDate, note? }` |
| POST | `/advances/payout` | Admin | Výplata přeplatku `{ houseId, amount, paymentDate, note? }` |
| POST | `/advances/opening-balance` | Admin | Počáteční stav `{ houseId, amount, isOverpayment, paymentDate, note? }` |
| DELETE | `/advances/{houseId}/{rowKey}` | Admin | Smazání platby libovolného typu (měsíční záloha v uzavřeném období → 409). |

## Nastavení záloh — `AdvanceSettingsFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/advance-settings` | přihlášený | Ceny a sazby. Členovi se nevrací koeficienty elektřiny ani přepisy záloh. |
| PUT | `/advance-settings` | Admin | `{ waterPricePerM3, waterPriceValidFrom, waterPriceValidTo?, monthlyElectricityCost, electricityCoefficients: {houseId: %}, monthlyCommonBaseFee, houseOverrides: {houseId: {waterAdvance, electricityAdvance, commonAdvance}}, lossAllocationMethod }` — koeficienty musí dát 100 % (±0,1). |
| GET | `/advance-settings/calculate` | přihlášený | Doporučené a aktuální zálohy per dům ([VYUCTOVANI.md §4](VYUCTOVANI.md#4-výpočet-doporučených-záloh)). Člen jen vlastní dům. |

## Zúčtovací období a vyúčtování — `BillingPeriodFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/billing-periods` | přihlášený | Období vč. součtu faktur. |
| POST | `/billing-periods` | Admin | `{ name, dateFrom, dateTo }` |
| PUT | `/billing-periods/{id}` | Admin | Stejné tělo. 409, pokud není otevřené. |
| GET | `/billing-periods/{id}/calculate?method=` | Admin | Náhled vyúčtování, nic neukládá. `method` = `Equal` (výchozí) \| `ProportionalToConsumption`. |
| POST | `/billing-periods/{id}/close` | Admin | `{ lossAllocationMethod, fundDrawAmount, applyNewWaterPrice, newWaterPriceValidFrom? }` — **nevratné**. |
| GET | `/billing-periods/{id}/settlements` | přihlášený | Uložená vyúčtování. Člen jen vlastní dům. |
| GET | `/billing-periods/{id}/settlements/{houseId}/pdf` | přihlášený | PDF pro jeden dům (jen uzavřené období). Člen jen vlastní dům. |
| GET | `/billing-periods/{id}/pdf` | Admin | ZIP se všemi PDF. |

## Dokumenty — `DocumentFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/documents?category=` | přihlášený | Seznam (kategorie `stanovy`, `zapisy`, `smlouvy`, `ostatni`). |
| POST | `/documents?name=&category=` | Admin | Raw tělo souboru, `Content-Type` = typ souboru (PDF, DOCX, XLSX, JPEG, PNG), max 20 MB. |
| GET | `/documents/{id}/download` | přihlášený | Stažení aktuální verze. |
| DELETE | `/documents/{id}` | Admin | Smaže dokument (historie verzí v úložišti zůstává). |
| POST | `/documents/{id}/versions` | Admin | Nová verze; drží se posledních 10. |
| GET | `/documents/{id}/versions` | přihlášený | Historie verzí. |
| GET | `/documents/{id}/versions/{version}/download` | přihlášený | Stažení konkrétní verze. |

## Hospodaření — `FinanceFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/finance?year=&category=` | přihlášený | Příjmy a výdaje (kategorie se uplatní jen spolu s rokem). |
| GET | `/finance/summary?year=` | přihlášený | Souhrn po kategoriích (rok povinný). |
| GET | `/finance/balance` | přihlášený | Kumulativní stav účtu spolku. |
| GET | `/finance/fund` | Admin, Účetní | Zůstatek společného fondu ([VYUCTOVANI.md §6](VYUCTOVANI.md#6-společný-fond)). |
| POST | `/finance` | Admin | `{ type: Income\|Expense, category, amount, date, description }` |
| PUT | `/finance/{id}` | Admin | Stejné tělo. |
| POST | `/finance/{id}/attachment` | Admin | PDF příloha (max 20 MB). |
| GET | `/finance/{id}/attachment` | Admin, Účetní | Stažení PDF. |
| GET | `/finance/export/pdf?year=` | Admin, Účetní | Roční přehled v PDF. |
| GET | `/finance/export/xlsx?year=` | Admin, Účetní | Roční přehled v Excelu. |

Kategorie: `voda`, `elektro`, `udrzba`, `pojisteni`, `jine`, systémová `fond-voda` (čerpání fondu při uzavření období).

## Notifikace — `NotificationFunctions.cs`, `Triggers/ReadingReminderTrigger.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| POST | `/notifications/send` | Admin | `{ type, periodId?, year?, month? }`, `type` = `reading_reminder` \| `import_completed` (vyžaduje `year`, `month`) \| `settlement_closed` (vyžaduje `periodId`). |
| *timer* | `ReadingReminderTimer` | — | CRON `0 0 8 1 * *` — připomínka odečtu 1. dne v měsíci. |

## Audit — `AuditFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/audit-log?from=&to=&entityType=&entityId=` | Admin | Záznamy auditu (X4), nejnovější první. Data `RRRR-MM-DD`, výchozí posledních 90 dní, `to` včetně celého dne, rozsah max. 2 roky. |

## Nákladové složky — `CostComponentFunctions.cs`

Nový model (T02). Data `RRRR-MM-DD`, metody `Metered` | `Equal` | `Ratio` | `Percent`, základ `Metered` | `CostEntries`. Porušení pravidel vrací 400 `{ error, errors: [{ field: "", message }] }` se všemi důvody. Každý zápis jde do auditu.

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/cost-components` | Admin, Accountant | Seznam složek s dnešní metodou a počtem účastníků. |
| GET | `/cost-components/{id}` | Admin, Accountant | Detail: složka, pravidla, účasti (se jménem domu), `lastClosedDay`. |
| GET | `/cost-components/{id}/segments?from=&to=` | Admin, Accountant | Úseky, ve kterých je rozpočet konstantní (metoda + účastníci), max. 10 let. |
| POST | `/cost-components` | Admin | `{ name, code, startDate, allocationBasis, waterRole?, method?, note? }` (`waterRole` = `Consumption` \| `Losses` jen u složky podle odečtů, každá role nejvýš jednou) → 201. Kód `A–Z0–9_` (převede se na velká), unikátní (409). Založí i první pravidlo od `startDate` (výchozí `Metered` u měřených, jinak `Equal`). |
| PUT | `/cost-components/{id}` | Admin | `{ name, active, note? }` — kód, start a základ se nemění. |
| POST | `/cost-components/{id}/rules` | Admin | `{ validFrom, method, ratioSource?, reason }` → 201. Důvod povinný. Otevřené pravidlo před `validFrom` se ukončí den předem. |
| DELETE | `/cost-components/{id}/rules/{ruleId}?reason=` | Admin | Smaže pravidlo zadané omylem, předchozí pravidlo se prodlouží. Poslední pravidlo smazat nelze. |
| POST | `/cost-components/{id}/participations` | Admin | `{ houseId, validFrom, validTo?, weight?, reason? }` → 201. Bez překryvu u stejného domu, `Percent` = 100 % v každém dni, ne před startem složky. |
| POST | `/cost-components/{id}/participations/{pid}/end` | Admin | `{ validTo, reason? }` — poslední den účasti (lze i posunout). |
| DELETE | `/cost-components/{id}/participations/{pid}?reason=` | Admin | Smaže účast zadanou omylem. |

Změna, která zasahuje do uzavřeného období (mezizávěrka, T08), se odmítne.

## Počáteční stavy — `OpeningBalanceFunctions.cs`

Nový model (T03). Typy `MeterReading` (m³) | `FundShare` (Kč, **kladné = dům má u spolku přeplatek**, X1) | `ComponentCredit` (Kč, záporné = kredit u dodavatele). Klíč stavu je `{typ}|{cíl}|{období vlastnictví nebo -}`; v URL se kóduje (`|` = `%7C`).

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/ownership-periods` | Admin, Accountant | Období vlastnictví všech domů. |
| POST | `/ownership-periods/start` | Admin | `{ startDate }` — každý aktivní dům bez období dostane období od `startDate` (vlastník = kontaktní osoba). Idempotentní. |
| GET | `/opening-balances` | Admin, Accountant | Všechny počáteční stavy se jmény domů, vodoměrů a složek; `locked` = datum je za mezizávěrkou. |
| GET | `/opening-balances/component-credit-preview?componentId=&date=&value=` | Admin, Accountant | Jak se kredit složky rozdělí mezi domy podle pravidla a účasti k datu (S2: −20 000 → 4 × −5 000). |
| POST | `/opening-balances` | Admin | `{ type, houseId?, meterId?, componentId?, date, value, isEstimate, source, note? }` → 201; 409, pokud pro stejnou kombinaci stav už je. `source` povinný. Stav vodoměru se zapíše i jako odečet k datu (odhad s poznámkou); jiný existující odečet se nepřepíše. |
| PUT | `/opening-balances/{key}` | Admin | Stejné tělo + `reason?`. Za mezizávěrkou jen s důvodem (audit `Correction`). |
| DELETE | `/opening-balances/{key}?reason=` | Admin | Smaže stav zadaný omylem (ne za mezizávěrkou); odečet zůstává. |

## Nákladové záznamy — `CostEntryFunctions.cs`

Nový model (T06). Typy `Advance` | `Settlement` (kladné = doplatek, záporné = přeplatek) | `OneOff`, úhrada `Bank` | `SupplierCredit` | `Cash` | `Other` (jen evidence, na rozpočet nemá vliv, R6).

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/cost-components/{id}/entries?from=&to=` | Admin, Accountant | Záznamy složky, jejichž období se překrývá s rozsahem. |
| GET | `/cost-components/{id}/entries/{entryId}/allocation` | Admin, Accountant | Rozpad záznamu: úseky (stálá účast a metoda, řezy po měsících), částka úseku podle dní, podíl každého domu a součty za domy. U složky podle odečtů 400. |
| POST | `/cost-components/{id}/entries` | Admin | `{ type, periodFrom, periodTo, amount, quantityM3?, supplier?, documentId?, paidFrom, note?, reason? }` → 201. Ne před startem složky, záloha > 0, u složky podle odečtů povinné `quantityM3`. Záznam, který nejde rozpočítat (nikdo se neúčastní), se odmítne. |
| POST | `/cost-components/{id}/entries/recurring` | Admin | `{ amount, periodicity: Monthly\|Quarterly\|HalfYearly\|Yearly, from, to, supplier?, paidFrom, note? }` → 201 se sérií záloh. Celá série se ověří před zápisem, max. 120. |
| PUT | `/cost-components/{id}/entries/{entryId}` | Admin | Stejné tělo jako POST. |
| DELETE | `/cost-components/{id}/entries/{entryId}?reason=` | Admin | Smaže záznam. |

Záznam, jehož období začíná za mezizávěrkou, nelze přidat, změnit ani smazat.

## Voda a ztráty — `WaterSettlementFunctions.cs`

Nový model (T05). Potřebuje složku s rolí `Consumption` (Voda PVK, faktury s m³) a volitelně složku s rolí `Losses` (ztráty, metoda `Equal` nebo `Ratio` s `ratioSource`).

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/water-settlement?from=&to=` | Admin, Accountant | Pro každý interval mezi odečty hlavního vodoměru: spotřeba hlavního a domovních vodoměrů (chybějící hraniční odečet se interpoluje, označí se odhad), ztráta, cena za m³ z faktur PVK (Σ Kč ÷ Σ m³ podle dní), náklad vody a ztrát po domech, metoda ztrát po úsecích a rozdíl proti fakturám. Záporná ztráta se nerozpočítá (varování). Max. 5 let. |

## Saldo domu — `LedgerFunctions.cs`

Nový model (T07). **Znaménko salda (X1): kladné = přeplatek (spolek dluží domu), záporné = nedoplatek.** Částky položek jsou jejich vliv na saldo (platby a kredity kladně, náklady záporně).

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/ledger/houses/{houseId}?ownershipPeriodId=&from=&to=` | přihlášený; Member jen svůj dům (403) | Saldo domu: počáteční podíl ve fondu, platby (zálohy k 1. dni měsíce, doplatky a výplaty ke dni platby; starý „počáteční zůstatek“ se nezapočítává), rozpočtené náklady po složkách a úsecích (kredit složky, voda, ztráty) s rozpadem výpočtu, průběžné saldo. Výchozí období vlastnictví = aktuální vlastník, výchozí rozsah = start účtování … dnes. |
| GET | `/ledger/houses/{houseId}/export?format=xlsx\|csv&…` | jako detail | Export salda domu (XLSX, nebo CSV pro český Excel: UTF-8 s BOM, „;“, desetinná čárka). |
| GET | `/ledger/overview/export?format=xlsx\|csv&from=&to=` | přihlášený | Export přehledu včetně řádků „Σ domů“ a „Rozpočteno složkou“. |
| GET | `/ledger/overview?from=&to=` | přihlášený | Všechny domy × složky (bez osobních údajů): počáteční podíl, platby, náklady po složkách, saldo; kontrolní řádek za každou složku (rozpočteno vs. Σ domů) s varováními. |

## Systém — `SystemFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/environment` | veřejné | `{ environment }` = `dev` \| `test` \| `prod` \| `unknown` podle app settingu `Environment` (T01). |

## Seed — `SeedFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| POST | `/seed` | veřejné | Založí výchozí domy, vodoměry a admina. Funguje jen s `ENABLE_SEED=true`, jinak 404. Idempotentní. **V PROD musí být vypnuto.** |

---

## Chyby

Chybové odpovědi mají tvar:

```json
{ "error": "Lidsky čitelná zpráva v češtině." }
```

Validační chyby (FluentValidation) navíc obsahují seznam polí:

```json
{
  "error": "Formulář obsahuje chyby.",
  "errors": [ { "field": "DateTo", "message": "…" } ]
}
```

| Kód | Význam |
|-----|--------|
| 200 / 201 / 204 | OK / vytvořeno / smazáno |
| 400 | neplatný vstup, validace |
| 401 | chybí nebo je neplatný token (`Nejste přihlášeni.`) |
| 403 | chybí role, cizí dům, nebo uživatel není registrován |
| 404 | záznam nenalezen (i vypnutý seed) |
| 409 | konflikt — uzavřené období, duplicita |
| 500 | neočekávaná chyba (`Nastala neočekávaná chyba.`) |

Aplikační chyby vyhazuj jako `AppException(message, statusCode)` (`Oaza.Application/Exceptions/AppException.cs`); endpoint je převede na odpověď výše.
