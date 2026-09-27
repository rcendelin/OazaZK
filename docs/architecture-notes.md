# Poznámky k architektuře (T00)

Rozcestník podle osnovy zadání. Podrobnosti jsou v samostatných dokumentech; tady je jen to podstatné
a odkaz, kde hledat dál. Stav k 27. 9. 2026.

## Stack

| Vrstva | Technologie | Kde |
|---|---|---|
| Frontend | React 19 + TypeScript (strict), Vite, Tailwind 4, React Router 7, MSAL | `web/`, [ARCHITEKTURA §6](ARCHITEKTURA.md#6-frontend) |
| Backend | .NET 8 Azure Functions (isolated worker, Consumption plan), Clean Architecture | `api/src/`, [ARCHITEKTURA §2](ARCHITEKTURA.md#2-vrstvy-backendu-clean-architecture) |
| Data | Azure Table Storage (repozitáře nad `Azure.Data.Tables`, žádné EF ani JOINy) + Blob Storage (dokumenty, přílohy, PDF) | [ARCHITEKTURA §3](ARCHITEKTURA.md#3-datový-model) |
| E-mail | Azure Communication Services (magic link, připomínky odečtů) | [ARCHITEKTURA §7](ARCHITEKTURA.md#7-e-maily-a-notifikace) |

Vrstvy backendu: `Oaza.Domain` (entity, rozhraní, čisté doménové služby — např. `Services/Allocator`) →
`Oaza.Application` (use casy, DTO, validátory) → `Oaza.Infrastructure` (Table/Blob repozitáře, e-mail, JWT) →
`Oaza.Functions` (HTTP triggery, middleware autentizace a rolí, DI).

## Entity a tabulky

Přehled tabulek, klíčů (PartitionKey / RowKey) a blob kontejnerů: [ARCHITEKTURA §3](ARCHITEKTURA.md#3-datový-model).
Hlavní entity: `User`, `House`, `WaterMeter`, `MeterReading`, `BillingPeriod`, `SupplierInvoice` (+ řádky),
`AdvancePayment` (typy záloha / doplatek / výplata / počáteční stav), `AdvanceSettings`, `Settlement`,
`Document` (+ verze), `FinancialRecord`.

Rozpracované mimo tento řetěz PR: import bankovního výpisu přidává `BankAccountMappings` a `BankTransactions` (PR #4).

## Výpočet vody, záloh a salda

Jak to dnes počítá kód, včetně vzorců a známých nekonzistencí: [VYUCTOVANI.md](VYUCTOVANI.md).

- **Vyúčtování vody** — spotřeba podružných vodoměrů, ztráta = hlavní − Σ podružných, rozpočet faktur podle
  podílu (§2), uzavření období se snapshotem `Settlement` (§3).
- **Doporučené zálohy** — průměrná spotřeba + podíl ztráty × cena, elektřina podle koeficientů, společný základ (§4).
- **Saldo domu** — jedno čisté saldo, rozpad na složky je jen informativní (§5), společný fond (§6).
- **Import odečtů** — Excel / schránka, dvoukrokový náhled a potvrzení (§7).

Nový výpočetní model (nákladové složky, ledger, mezizávěrky) podle zadání teprve vzniká — viz
[gap-analysis.md](gap-analysis.md). Starý výpočet se nemigruje, zanikne (rozhodnutí X2 v [open-questions.md](open-questions.md)).

## Role

`Admin` (vše), `Accountant` (čtení všech financí a dokumentů, zápis dle tasku), `Member` (jen vlastní dům a sdílené dokumenty).
Role je v entitě `User` a v JWT; endpointy se omezují atributem `[RequireRole]`. Detail a matice: [ARCHITEKTURA §5](ARCHITEKTURA.md#5-role-a-oprávnění).

## Nasazení

Tři prostředí se samostatnými resource groupami, storage, Functions a SWA:
`develop` → DEV, `release/**` → TEST, `master` → PROD ([DEPLOYMENT-TEST-PROD.md](DEPLOYMENT-TEST-PROD.md), [DEPLOYMENT-DEV.md](DEPLOYMENT-DEV.md)).
Prostředí hlásí API přes `GET /api/environment` (app setting `Environment`) a UI mimo prod zobrazuje pruh (T01).
PROD zatím nikdy neběžel.

## Testy

- xUnit + FluentAssertions + Moq: `Oaza.Domain.Tests`, `Oaza.Application.Tests`, `Oaza.Functions.Tests`.
- Integrační testy repozitářů proti Azurite (`Oaza.Infrastructure.Tests`, `[SkippableFact]`).
- Frontend: jen `tsc -b`, ESLint (CI gate) a build; E2E (Playwright) zatím není.
- Lokální spuštění a testy: [LOKALNI-VYVOJ.md](LOKALNI-VYVOJ.md).
