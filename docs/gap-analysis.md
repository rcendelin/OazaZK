# Gap analýza T01–T14 (T00)

Co ze zadání existuje, co chybí a jak se přechází. Zadání je v `docs/ZADANI.md`, podrobný checklist po úkolech
se vede v přehledu úkolů mimo repozitář (artefakt „Portál Oáza – přehled úkolů k implementaci“); tady je souhrn. Stav k 27. 9. 2026.

**Migrace dat:** žádná. Produkční data neexistují, platí jen nový model a testovací instance se přeseeduje
(rozhodnutí X2, [open-questions.md](open-questions.md)). „Migrace“ níže znamená, který starý kód nový nahradí.

| Task | Existuje | Chybí (hlavní) | Nahrazuje / migrace kódu |
|---|---|---|---|
| **T01** Prostředí test / prod | 3 workflow a oddělené RG; `Environment` + `GET /api/environment`, pruh v UI (PR #6) | `infra/` skript, test izolace dat, Required reviewers, hand-off PROD | — |
| **T02** Nákladové složky a účast | `AdvanceSettings` (koeficienty elektřiny, globální metoda ztrát); alokační knihovna `Allocator` (PR #5) | `CostComponent`, `ComponentAllocationRule`, `Participation`, validace, úseky, API, UI | koeficienty a `LossAllocationMethod` v `AdvanceSettings` |
| **T03** Počáteční stavy, nový majitel | `PaymentType.OpeningBalance` (jen čistý zůstatek) | `OwnershipPeriod`, `OpeningBalance` (3 typy), převod domu, průvodce | `PaymentType.OpeningBalance` |
| **T04** Odečty: odhady | import Excel / schránka s náhledem | `IsEstimate`, `EstimateReading` (interpolace), UI odlišení, zámek mezizávěrky | — (aditivní) |
| **T05** Ztráty vody | ztráta, metody Equal / Proportional | záporná ztráta jako varování, náklad = m³ × cena, metoda z pravidla složky | `AllocateLoss` ve starém výpočtu |
| **T06** Nákladové záznamy | `SupplierInvoice` s řádky, `FinancialRecord` s přílohou | `CostEntry` (ADVANCE / SETTLEMENT / ONE_OFF), rozprostření po dnech, UI | `SupplierInvoice`, rozpočtové sazby v `AdvanceSettings` |
| **T07** Saldo (ledger) | `CalculateHouseSaldoUseCase`, `/saldo` | `GetHouseLedger`, drill-down, kontrolní řádek, exporty, nová znaménková konvence | `CalculateHouseSaldoUseCase` |
| **T08** Mezizávěrky | uzavření `BillingPeriod` + snapshot `Settlement` | `InterimClosing` (ALL / HOUSE), dělení přes řez, neměnnost, roční závěrka | `BillingPeriod`, `Settlement`, uzavření období |
| **T09** Pokladna | nic | `CashBookEntry`, pravidla zůstatku a storna, UI, exporty | — |
| **T10** Oddělený fond | nic (existující fond je fond spolku) | feature flag, entity fondu, izolační testy | — |
| **T11** Podklady od Radky | upload dokumentu po jednom, verze | hromadný upload, vazba dokument ↔ `CostEntry`, nezaúčtované | — |
| **T12** Nápověda | „Jak to funguje“, `help.ts`, kontextová nápověda | texty k novému modelu, návod pro správce, E2E | — |
| **T13** Seed prod | `POST /seed` (fiktivní data) | CSV šablony, dry-run / apply, idempotence, report | `/seed` pro prod |
| **T14** E2E a release | nic | Playwright S1–S8, release checklist, demo data | — |

## Průřezové předpoklady

Před T02–T10: znaménková konvence salda (X1), jen nový model (X2), alokační knihovna (X3 — hotovo),
audit log (X4), kalendářní data `Europe/Prague` (X5), procesní infrastruktura (X6: changelog hotovo,
pokrytí v CI, build bez warningů, Playwright, FsCheck).

## Doporučené pořadí

1. Průřezové: X4, X5, zbytek X6.
2. T02 (složky a účast) → T06 (náklady) → T05 (ztráty) → T08 (mezizávěrky) → T07 (ledger).
3. T03 a T04 (počáteční stavy a odhady) souběžně s T08.
4. T09, T11, T10 (nezávislé moduly), T12 průběžně.
5. T13 a T14 na konec před spuštěním PROD.
