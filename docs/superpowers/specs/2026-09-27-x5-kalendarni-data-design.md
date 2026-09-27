# X5 – Kalendářní data a časové pásmo `Europe/Prague` — návrh

**Datum:** 2026-09-27
**Autor:** smyčka nad TASK.md (Claude) pro Rosťu Čendelína
**Stav:** návrh ke schválení — nic se neimplementuje, dokud se neodsouhlasí §4

## 1. Kontext

Pravidlo 6 zadání: účetní data jsou **kalendářní dny v `Europe/Prague`** a období jsou **uzavřené intervaly
`[od, do]` po dnech**. Nový výpočetní model (T02 účast, T05 ztráty pro-rata, T06 rozprostření záloh po dnech, T08 řezy
mezizávěrek) na tom přímo stojí — jeden posunutý den mění rozpočet.

Kód dnes pracuje s `System.DateTime` a do Table Storage ukládá UTC (`DateTimeOffset`).

## 2. Inventura (stav `develop` k 27. 9. 2026)

**Datumová pole v entitách** (22 polí ve 14 entitách):

| Druh | Pole | Jak se dnes ukládá |
|---|---|---|
| **Kalendářní den** (účetně významný) | `MeterReading.ReadingDate`, `AdvancePayment.PaymentDate` (+ `Year`/`Month`), `FinancialRecord.Date`, `BankTransaction.Date`, `InvoiceLineItem.DateFrom/DateTo`, `SupplierInvoice.IssuedDate/DueDate`, `BillingPeriod.DateFrom/DateTo`, `AdvanceSettings.WaterPriceValidFrom/To`, `WaterMeter.InstallationDate` | `DateTime` s půlnocí v **UTC** (`SpecifyKind(..., Utc)` na ~40 místech), čas se zahazuje `.Date` (~34 míst) |
| **Okamžik** (kdy se něco stalo) | `ImportedAt`, `UploadedAt`, `UpdatedAt`, `AuditLogEntry.Timestamp`, `User.LastLogin`, `MagicLinkExpiry`, `MagicLinkRequestWindowStart` | `DateTime.UtcNow` |

Klíče: `MeterReading` má RowKey = obrácený čas z `ReadingDate`, `AdvancePayment` RowKey `YYYY-MM`,
`AuditLog` PartitionKey = UTC měsíc.

**Konvence „půlnoc UTC = kalendářní den“** se v backendu drží konzistentně a funguje: den se nikdy nepřevádí přes
časové pásmo, jen se nese. Problém je **„dnešek“** a **frontend**:

| # | Místo | Problém | Dopad |
|---|---|---|---|
| D1 | `ReadingsImportPage.tsx` (2×), `SaldoPage.tsx` (`isoToday`), `AuditLogPage.tsx`, `ConsumptionChart.tsx` | výchozí „dnes“ = `new Date().toISOString().slice(0, 10)` → **UTC** datum | mezi 0:00 a 1:00 (zima) / 2:00 (léto) pražského času formulář nabídne **včerejšek**; odečet nebo platba zadaná v noci 1. dne měsíce spadne do předchozího měsíce |
| D2 | validátory (`CreateReadingRequestValidator` aj., 9×) | „ne v budoucnosti“ = `DateTime.UtcNow.AddDays(1)` | tolerance 1 den to maskuje; bez ní by v noci odmítl dnešní datum |
| D3 | PDF (`GenerateSettlementPdfUseCase`, `GenerateFinanceReportUseCase`) | „Datum vystavení“ = `DateTime.UtcNow` | v noci na PDF včerejší datum |
| D4 | `BillingPage.tsx:473` | `dateTo + 86 400 000 ms` → posun o 24 h | ve dnech změny času (březen/říjen) 23/25 h → správně jen díky UTC půlnoci |
| D5 | `DateTime` v doméně | typ nenese „je to den, ne okamžik“ | nový kód snadno smíchá den a okamžik; porovnání `<=` s časem ≠ půlnoc vynechá poslední den intervalu |

Nic z toho dnes nekazí uložená data — produkční data navíc neexistují (X2).

## 3. Možnosti

| Varianta | Co | Pro | Proti |
|---|---|---|---|
| **A – `DateOnly` všude** | Přepsat všechna kalendářní pole na `DateOnly` včetně starého modelu | typově čisté | velký zásah do kódu, který podle X2 zanikne; změna mapperu, DTO, frontendu najednou |
| **B – `DateOnly` v novém modelu + `IClock`** *(doporučeno)* | Nové entity T02–T10 používají `DateOnly` a `DateRange`; „dnes“ se všude bere z `IClock.Today` v `Europe/Prague`; frontend `todayIso()`; starý model jen opravit D1–D3 | zásah úměrný X2 (starý model zanikne), typová bezpečnost tam, kde se počítá; opraví reálné chyby hned | dvě reprezentace dne po dobu přechodu (jen v kódu, který zanikne) |
| C – jen konvence | Nechat `DateTime`, sepsat pravidla | nic neláme | D5 zůstává — v novém výpočtu nejdražší chyba |

## 4. Návrh (varianta B)

1. **`IClock`** (Domain) s `DateOnly Today` a `DateTimeOffset Now`; implementace `PragueClock` přes
   `TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague")` (na Linux Functions je IANA id; na Windows fallback
   `"Central Europe Standard Time"`), registrace v DI, v testech pevné hodiny. Postavené na `TimeProvider` (už v DI od X4).
2. **`DateRange`** (Domain, `readonly record struct`): uzavřený interval `[From, To]` po dnech — `Days`
   (= `To − From + 1`), `Contains`, `Overlaps`, `Intersect`, `SplitAt(DateOnly cut)` (řez mezizávěrky: `[From, cut − 1]`
   + `[cut, To]`), validace `From ≤ To`. Unit testy + FsCheck (Σ dnů úseků = dny celku).
3. **Nové entity (T02–T10)**: kalendářní pole jako `DateOnly`. V Table Storage jako řetězec `yyyy-MM-dd`
   (lexikograficky řaditelný, bez časového pásma); okamžiky dál UTC `DateTimeOffset`.
   DTO: `DateOnly` se v JSON serializuje jako `"yyyy-MM-dd"` (System.Text.Json to umí od .NET 7).
4. **Starý model** (zanikne podle X2): nepřepisovat typy, jen opravit D1–D3:
   - frontend `web/src/utils/date.ts`: `todayIso()` z lokálních složek data (`getFullYear/getMonth/getDate`),
     nahradit všech 6 výskytů; posílat na API jen `yyyy-MM-dd` nebo `…T00:00:00Z` z řetězce, ne `new Date(x).toISOString()`,
   - validátory a PDF: „dnes“ z `IClock.Today`.
5. **Frontend zobrazování**: `Intl.DateTimeFormat('cs-CZ', { timeZone: 'Europe/Prague' })` pro okamžiky; kalendářní dny
   formátovat z řetězce `yyyy-MM-dd` bez převodu přes `Date` (žádný posun v jiné zóně prohlížeče).
6. **Pravidlo do `CLAUDE.md`**: „kalendářní den = `DateOnly` + `DateRange` (uzavřený interval), okamžik = UTC; ‚dnes‘ jen
   z `IClock` / `todayIso()`; nikdy `toISOString().slice(0, 10)` ani `DateTime.UtcNow.Date` pro účetní den.“

## 5. Plán (po schválení)

| Krok | Obsah | Odhad |
|---|---|---|
| 1 | `todayIso()` + nahrazení 6 výskytů (D1), smoke test v Playwright | malý |
| 2 | `IClock`/`PragueClock` v DI, validátory a PDF (D2, D3), testy přelomu dne 23:30/0:30 UTC v zimě i v létě | malý |
| 3 | `DateRange` + testy (vč. FsCheck), pravidlo v `CLAUDE.md` | malý |
| 4 | T02 a další už rovnou s `DateOnly`/`DateRange` | součást T02+ |

## 6. Otázky k rozhodnutí

1. Souhlas s variantou B (nový model `DateOnly`, starý jen opravit)?
2. Má „dnes“ pro uzávěrky a PDF opravdu znamenat pražský kalendářní den i pro uživatele mimo ČR? (Předpoklad: ano, jde o účetnictví spolku v Praze.)
