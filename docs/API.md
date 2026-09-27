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
| GET | `/readings/export?format=xlsx\|csv&from=&to=` | Admin, Účetní | Export odečtů (T04): datum, vodoměr, dům, stav a spotřeba od předchozího odečtu (m³, 3 desetinná místa), sloupce „Odhad“ (ano/ne) a „Popis odhadu“, zdroj. `from`/`to` volitelné (`yyyy-MM-dd`). CSV pro český Excel (UTF-8 s BOM, „;“, desetinná čárka). |
| GET | `/readings/estimate?meterId=&date=` | Admin | Odhad stavu vodoměru k datu (T04): skutečný odečet, interpolace po dnech, nebo nejbližší odečet, když existuje jen jedna strana ([VYUCTOVANI §7.5](VYUCTOVANI.md#75-odhad-odečtu-t04)). Nic neukládá. |
| POST | `/readings` | Admin | `{ meterId, readingDate, value, isEstimate?, estimateNote? }` — ruční odečet; odhad vyžaduje `estimateNote`. |
| PUT | `/readings/{meterId}/{yyyy-MM-dd}` | Admin | `{ value, newDate? }` — oprava hodnoty/přesun data. 409 v uzavřeném období. |
| POST | `/readings/import` | Admin | Excel (multipart nebo raw body, max 5 MB) → náhled. Nic neukládá. |
| POST | `/readings/import/clipboard` | Admin | `{ text, readingDate }` — tabulátorový text s hlavičkou `Address`, `Value 1` → náhled. |
| POST | `/readings/import/confirm` | Admin | `{ readings: [{ meterId, readingDate, value }] }` — odečty z náhledu; server je znovu ověří a uloží všechny, nebo žádný ([VYUCTOVANI §7.4](VYUCTOVANI.md#74-potvrzení)). 400 chyba validace, 409 odečet přibyl od náhledu. |

Formát souborů a validační pravidla: [VYUCTOVANI.md §5](VYUCTOVANI.md#5-import-odečtů).

## Zálohy a platby — `AdvanceFunctions.cs`

Saldo domu je v části [Saldo domu](#saldo-domu--ledgerfunctionscs) (starý `GET /advances/saldo` je odstraněn, X2).

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/advances?houseId=&year=` | přihlášený | Platby. Člen jen vlastní dům (cizí `houseId` → 403). |
| POST | `/advances` | Admin | Měsíční záloha `{ houseId, year, month, waterAmount, electricityAmount, commonAmount, paymentDate }`. 409 při duplicitě. |
| PUT | `/advances/{houseId}/{yyyy-MM}` | Admin | `{ waterAmount, electricityAmount, commonAmount, paymentDate }`. 409 v období uzavřeném mezizávěrkou. |
| POST | `/advances/doplatek` | Admin | `{ houseId, waterAmount, electricityAmount, commonAmount, paymentDate, note? }` |
| POST | `/advances/payout` | Admin | Výplata přeplatku `{ houseId, amount, paymentDate, note? }` |
| POST | `/advances/opening-balance` | Admin | Počáteční stav `{ houseId, amount, isOverpayment, paymentDate, note? }` — saldo domu ho ignoruje (náhradou je podíl na fondu, T03); UI ho už nenabízí. |
| DELETE | `/advances/{houseId}/{rowKey}` | Admin | Smazání platby libovolného typu (platba v období uzavřeném mezizávěrkou → 409). |

## Nastavení záloh — `AdvanceSettingsFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/advance-settings` | přihlášený | `{ houseOverrides }` — ruční přepisy záloh. Členovi se vrací prázdné. |
| PUT | `/advance-settings` | Admin | `{ houseOverrides: {houseId: {waterAdvance, electricityAdvance, commonAdvance}} }` — nahradí všechny přepisy; záporná částka → 400. |
| GET | `/advance-settings/calculate` | přihlášený | `{ from, to, months, houses: [{ houseId, houseName, costsInPeriod, recommended, actual, hasOverride }] }` — doporučené zálohy z nákladů domu za posledních 12 měsíců ÷ 12 ([VYUCTOVANI.md §3](VYUCTOVANI.md#3-doporučené-zálohy)); složky `{ water, electricity, common, total }`. Člen jen vlastní dům. |

## Dokumenty — `DocumentFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/documents?category=` | přihlášený | Seznam (kategorie `stanovy`, `zapisy`, `smlouvy`, `faktury`, `ostatni`). |
| POST | `/documents?name=&category=&componentId=` | Admin | Raw tělo souboru, `Content-Type` = typ souboru (PDF, DOCX, XLSX, JPEG, PNG), max 20 MB. Kategorie `faktury` (T11) jen PDF a obrázky, volitelně s nákladovou složkou `componentId`. Pravidla v `DocumentUploadRules`. |
| GET | `/documents/{id}/download` | přihlášený | Stažení aktuální verze. |
| GET | `/documents/unaccounted` | Admin, Accountant | Faktury a vyúčtování (kategorie `faktury`), na které zatím neodkazuje žádný nákladový záznam (T11), s názvem přiřazené složky. |
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
| GET | `/finance/fund` | Admin, Účetní | Zůstatek společného fondu ([VYUCTOVANI.md §4](VYUCTOVANI.md#4-saldo-domu-a-společný-fond)). |
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
| POST | `/notifications/send` | Admin | `{ type, year?, month? }`, `type` = `reading_reminder` \| `import_completed` (vyžaduje `year`, `month`). |
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
| POST | `/cost-components/{id}/entries` | Admin | `{ type, periodFrom, periodTo, amount, quantityM3?, supplier?, documentId?, paidFrom, note?, reason?, correctionOf? }` (zasahuje-li období do mezizávěrky: opravný záznam s `postingDate`, `reason` povinný) → 201. Ne před startem složky, záloha > 0, u složky podle odečtů povinné `quantityM3`. Záznam, který nejde rozpočítat (nikdo se neúčastní), se odmítne. |
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

## Mezizávěrky — `InterimClosingFunctions.cs`

Nový model (T08). Id mezizávěrky `{datum}|{All|House}|{dům nebo -}` se v URL kóduje.

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/interim-closings` | Admin, Accountant | Seznam od nejnovější: datum, rozsah, dům, důvod, kdo, součet salda, `canDelete` (jen poslední). |
| GET | `/interim-closings/{id}` | Admin, Accountant | Detail se snapshotem salda domů a saldem k datu přepočteným teď (`difference` ≠ 0 = něco se v uzavřeném období změnilo). |
| GET | `/interim-closings/{id}/export?format=xlsx\|csv` | Admin, Accountant | Export pro účetní: u mezizávěrky všech domů přehled salda za kalendářní rok do data řezu (`rocni-zaverka-RRRR.xlsx` k 31. 12.), u domu jeho saldo. Oddělený fond (T10) v něm nikdy není. |
| POST | `/interim-closings` | Admin | `{ date, scope: All\|House, houseId?, reason }` → 201. Datum nejpozději včera a později než dosavadní mezizávěrka (pro dům: všech domů i toho domu). Uloží snapshot salda. |
| DELETE | `/interim-closings/{id}?reason=` | Admin | Zruší jen poslední mezizávěrku, s důvodem. |

Po mezizávěrce se pravidla, účast, náklady, počáteční stavy, **odečty** (ruční zadání, oprava, přesun i import — 409 / chyba importu) a **platby** do data řezu nemění. Úprava nebo smazání platby s datem do řezu vrací 409; nová platba (záloha, doplatek, výplata, i z bankovního importu) s datem do řezu se zaúčtuje k prvnímu dni po řezu — záloha za uzavřený měsíc jako doplatek — a původní datum je v poznámce. Náklad, jehož období do řezu zasahuje, se zaúčtuje jako opravný záznam k prvnímu dni po řezu (`reason` povinný, odpověď nese `postingDate`).

## Převod domu — `HouseTransferFunctions.cs`

Nový model (T03, R7). Nový vlastník nedědí historii.

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/houses/{id}/transfer-preview?date=` | Admin | Náhled: původní vlastník, datum mezizávěrky (den před předáním), jeho závěrečné saldo (platby, náklady), vodoměr a návrh jeho stavu z odečtů, `problems` (co brání převodu). |
| POST | `/houses/{id}/transfer` | Admin | `{ transferDate, newOwnerName, newOwnerContact?, meterValue?, meterIsEstimate, meterSource?, fundShare (výchozí 0), updateHouseContact, reason? }` → mezizávěrka domu k `transferDate − 1`, ukončení období vlastnictví, nové období od `transferDate`, počáteční stavy nového vlastníka (fond, stav vodoměru jako odečet), volitelně nový kontakt domu. Vše v auditu. |

## Pokladna — `CashBookFunctions.cs`

Nový model (T09, R10). Záznamy se nemažou ani neupravují — oprava je storno.

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/cash-book?from=&to=` | přihlášený | Pokladní kniha: záznamy s průběžným zůstatkem (`effect` = vliv na zůstatek), počáteční a konečný zůstatek, příjmy a výdaje za období, `correctedBy` u stornovaných. |
| GET | `/cash-book/export?format=xlsx\|pdf&from=&to=` | Admin, Accountant | Export pokladní knihy pro účetní včetně příznaku „bez dokladu“. |
| POST | `/cash-book` | Admin, Accountant | `{ date, type: Deposit\|Expense, amount, category, description, counterparty?, hasReceipt, documentId?, bankTransactionRef?, componentId? }` → 201. Výdaj bez dokladu vyžaduje `counterparty`. Zůstatek nesmí být v žádný den záporný (ani zpětně). Ne do dne uzavřeného mezizávěrkou ani do budoucnosti. `componentId` u výdaje vytvoří jednorázový náklad složky hrazený hotově. |
| POST | `/cash-book/{id}/storno` | Admin, Accountant | `{ reason }` → 201 storno k dnešku; navázaný náklad se opraví záporným nákladem. Storno nesmí shodit zůstatek do mínusu, stornovat lze jednou. |

## Oddělený fond — `OffBookFundFunctions.cs`

Nový model (T10, O4). **Za přepínačem `OFF_BOOK_FUND_ENABLED` (app setting, výchozí vypnuto): když je vypnutý, všechny endpointy fondu vrací 404.** Fond je mimo účetnictví spolku; jeho data nečte saldo, mezizávěrky, pokladna ani exporty (hlídá test izolace).

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/features` | přihlášený | `{ offBookFund }` — které volitelné moduly jsou zapnuté (frontend podle toho skryje menu). |
| GET | `/off-book-funds` | přihlášený | Fondy se zůstatkem (příspěvky − výdaje z fondu − vyrovnání) a dluhem vůči těm, kdo platili předem. |
| GET | `/off-book-funds/{id}` | přihlášený | Detail: výzvy s tím, kdo zaplatil a kdo ne, pohyby. |
| POST | `/off-book-funds` | Admin | `{ name, purpose?, managerName, accountDescription?, active }` → 201. Celé číslo účtu se odmítne. |
| PUT | `/off-book-funds/{id}` | Admin | Stejné tělo. |
| POST | `/off-book-funds/{id}/records` | Admin | `{ kind: Call\|Contribution\|Expense\|Settlement, date, amount, text?, dueDate?, houseIds?, houseId?, callId?, method?, paidBy?, hasReceipt?, expenseId? }` → 201. Výdaj z fondu jen do výše zůstatku, jinak s `paidBy`; vyrovnání jen do výše dluhu. |
| DELETE | `/off-book-funds/{id}/records/{recordId}?reason=` | Admin | Smaže záznam zadaný omylem (ne výzvu s příspěvky ani výdaj s vyrovnáním). |

## Systém — `SystemFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| GET | `/environment` | veřejné | `{ environment }` = `dev` \| `test` \| `prod` \| `unknown` podle app settingu `Environment` (T01). |

## Import počátečních dat — `SeedImportFunctions.cs`

T13. Na všech prostředích (není za `ENABLE_SEED`), jen Admin. Tělo všech tří volání: `{ files: { "houses.csv": "…obsah CSV…", … } }` — názvy a sloupce souborů viz [seed/README.md](../seed/README.md).

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| POST | `/seed-import/dry-run` | Admin | Import nanečisto nad kopií dat, nic nezapíše. Vrací `{ applied, canApply, from, today, files: [{ file, uploaded, rows, created, unchanged, conflicts, errors }], issues: [{ file, line, severity: chyba\|konflikt, message }], houses: [{ houseName, opening, payments, costs, saldo }], components: [{ componentName, allocated, houses, matches, warnings }] }` (saldo X1: kladné = přeplatek). |
| POST | `/seed-import/report?format=md\|xlsx` | Admin | Report zkoušky jako soubor (Markdown / XLSX se 4 listy). |
| POST | `/seed-import/apply` | Admin | Zkouška, a když nemá chybu ani konflikt a je co založit, stejný import naostro (`applied = true`). Opakované volání nic nezmění. Audit: každý záznam + souhrn `SeedImport`. |

## Seed — `SeedFunctions.cs`

| Metoda | Cesta | Přístup | Popis |
|--------|-------|---------|-------|
| POST | `/seed` | veřejné | Fiktivní data pro vývoj (ostrá data importuje `/seed-import`). Založí výchozí domy, vodoměry a admina. Funguje jen s `ENABLE_SEED=true`, jinak 404. Idempotentní. **V PROD musí být vypnuto.** |

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
