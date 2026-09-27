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
| POST | `/readings` | Admin | `{ meterId, readingDate, value }` — ruční odečet. |
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
