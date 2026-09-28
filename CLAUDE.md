# CLAUDE.md — Oáza Zadní Kopanina Portal

## Project overview

Community portal for a small neighborhood association ("Oáza Zadní Kopanina") in Prague. 8 households, up to 15 users. Primary function: shared water supply management — monthly meter readings, cost allocation and house saldo, advance payments. Secondary: shared document storage and basic financial overview.

**Domain:** `oaza.cendelinovi.cz`
**Operator:** Single-person ops (Rosťa Čendelín)
**Language:** Czech UI, English code (variable names, comments, commit messages)

## Architecture

Three-layer Clean Architecture on Azure, cost-optimized for ~5–15 CZK/month:

```
React 19 SPA (Azure Static Web Apps, Free)
    ↕ HTTPS / REST API
.NET 10 Azure Functions (Flex Consumption plan, Isolated Worker)
    ↕ Azure.Data.Tables SDK
Azure Table Storage + Azure Blob Storage (LRS)
```

## Repository structure

Monorepo with two main directories. Human-facing docs live in `README.md` and `docs/` (Czech) — keep them in sync when behaviour changes (see "Documentation" below).

```
OazaZK/
├── README.md                     # Project overview, quickstart, doc index
├── CLAUDE.md                     # This file
├── .github/workflows/
│   ├── deploy-dev.yml            # develop    → DEV
│   ├── deploy-test.yml           # release/** → TEST
│   └── deploy.yml                # master     → PROD
├── api/                          # .NET 10 backend
│   ├── Oaza.sln                  # use this (Oaza.slnx is stale)
│   ├── global.json               # pins .NET 10 SDK
│   ├── src/
│   │   ├── Oaza.Domain/          # Entities, enums, constants, repository interfaces
│   │   ├── Oaza.Application/     # Use cases, DTOs, validators, mapping
│   │   ├── Oaza.Infrastructure/  # Table Storage repos, Blob Storage, ACS Email, JWT, Entra
│   │   └── Oaza.Functions/       # HTTP triggers, timer, DI setup, middleware, auth
│   │       └── local.settings.json.example
│   └── tests/
│       ├── Oaza.Domain.Tests/
│       ├── Oaza.Application.Tests/
│       ├── Oaza.Functions.Tests/
│       └── Oaza.Infrastructure.Tests/   # Azurite integration tests
├── web/                          # React 19 frontend (Vite 8, Tailwind 4 — CSS-configured, no tailwind.config)
│   └── src/
│       ├── api/                  # apiClient + one typed module per resource
│       ├── auth/                 # AuthContext, MSAL config, magic link flow
│       ├── components/           # Layout, ProtectedRoute, MetricCard, help/…
│       ├── content/help.ts       # single source of in-app help texts + glossary
│       ├── hooks/                # useApi
│       ├── pages/                # Route-level pages; pages/admin/ = Houses, Users, Meters
│       ├── types/                # TS interfaces mirroring API DTOs
│       └── utils/                # parseCzechNumber, Prague calendar days (date.ts)
└── docs/
    ├── ARCHITEKTURA.md           # layers, data model, auth, roles, frontend, CI/CD
    ├── VYUCTOVANI.md             # settlement / advances / saldo / fund / import — as implemented
    ├── API.md                    # full endpoint reference (generated from code)
    ├── LOKALNI-VYVOJ.md          # local dev setup, config keys, troubleshooting
    ├── DEPLOYMENT-DEV.md, DEPLOYMENT-TEST-PROD.md
    ├── ANALYZA-ADRESARE.md       # June 2026 code audit
    └── superpowers/              # specs + plans per feature
```

## Tech stack — backend

- **.NET 10** with Azure Functions Isolated Worker model on the **Flex Consumption** plan (`func-oaza-{env}-flex`; .NET 10 does not run on Linux Consumption)
- **Azure.Data.Tables** SDK for Table Storage (NOT EF Core — no relational DB)
- **ClosedXML** for Excel import/export (.xlsx parsing)
- **PdfSharpCore** for PDF generation (settlement sheets)
- **Azure Communication Services** for magic link emails and notifications
- **FluentValidation** for request validation
- **System.IdentityModel.Tokens.Jwt** for JWT generation/validation

### NuGet packages

**Verze pinuj explicitně — žádné floating wildcardy (`12.*`).** Floaty driftnou na `Azure.Core 1.55` (vyžaduje `Microsoft.Extensions.* >=10`) a rozbijou build proti pinům `8.*`. Verze jsou nyní pevné a `global.json` zamyká SDK na .NET 10 (`rollForward: latestFeature`).

```xml
<!-- Oaza.Infrastructure -->
<PackageReference Include="Azure.Data.Tables" />
<PackageReference Include="Azure.Storage.Blobs" />
<PackageReference Include="Azure.Communication.Email" />

<!-- Oaza.Application -->
<PackageReference Include="FluentValidation" />
<PackageReference Include="ClosedXML" />
<PackageReference Include="PdfSharpCore" />

<!-- Oaza.Functions -->
<PackageReference Include="Microsoft.Azure.Functions.Worker" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" />
<PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Http" />
<PackageReference Include="System.IdentityModel.Tokens.Jwt" />
<PackageReference Include="Microsoft.IdentityModel.Protocols.OpenIdConnect" />
```

## Tech stack — frontend

- **React 19** with TypeScript (strict mode)
- **Vite** as build tool
- **TailwindCSS** for styling
- **React Router v7** for routing
- **MSAL.js** (@azure/msal-browser) for Entra ID auth
- **Recharts** for charts
- No state management library — React Context + hooks sufficient for 15 users

### npm packages

See `web/package.json` for exact versions. Currently: React 19.2, React Router 7, Vite 8 (rolldown), Tailwind CSS 4 (`@tailwindcss/vite`, CSS-configured), TypeScript 5.9, MSAL browser 4 / react 3, Recharts 3, lucide-react icons, ESLint 9 with `eslint-plugin-react-hooks` 7 (React Compiler rules).

## Data model — Azure Table Storage

All entities use Azure Table Storage. No relational DB, no JOINs — all aggregation is done in application layer. Data volume is tiny (8 houses, ~100 readings/year).

### PartitionKey / RowKey strategy

| Entity | PartitionKey | RowKey | Rationale |
|--------|-------------|--------|-----------|
| User | `USER` | GUID | All users in one partition, tiny dataset |
| House | `HOUSE` | GUID | All houses in one partition |
| WaterMeter | `METER` | GUID | All meters in one partition |
| MeterReading | meter GUID | inverted timestamp (`DateTime.MaxValue.Ticks - readingDate.Ticks`) | Query latest readings per meter efficiently, newest first |
| AdvancePayment | house GUID | `YYYY-MM` (e.g. `2026-03`) | Query all payments for a house, filter by date range |
| Document | category string (e.g. `stanovy`, `zapisy`) | GUID | Query by category |
| FinancialRecord | `YYYY` (year) | GUID | Query by year |

### Entity definitions (C# domain models)

```csharp
// Oaza.Domain/Entities/User.cs
public class User
{
    public string Id { get; set; }             // GUID
    public string Name { get; set; }
    public string Email { get; set; }
    public UserRole Role { get; set; }         // Admin, Member, Accountant
    public string? HouseId { get; set; }       // FK to House (null for admin without house)
    public AuthMethod AuthMethod { get; set; } // EntraId, MagicLink
    public string? EntraObjectId { get; set; } // Entra ID object ID (nullable)
    public string? MagicLinkTokenHash { get; set; }            // SHA-256 hash of the token (never stored plaintext)
    public DateTime? MagicLinkExpiry { get; set; }
    public DateTime? LastLogin { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public int MagicLinkRequestCount { get; set; }             // rate limit: requests in the current window
    public DateTime? MagicLinkRequestWindowStart { get; set; } // rate limit: start of the 1h sliding window
    public int MagicLinkFailedAttempts { get; set; }           // lockout after 5 failed verifications
}

// Oaza.Domain/Entities/House.cs
public class House
{
    public string Id { get; set; }
    public string Name { get; set; }           // e.g. "Novákovi (142)"
    public string Address { get; set; }
    public string ContactPerson { get; set; }
    public string Email { get; set; }
    public bool IsActive { get; set; } = true;
}

// Oaza.Domain/Entities/WaterMeter.cs
public class WaterMeter
{
    public string Id { get; set; }
    public string MeterNumber { get; set; }    // Physical meter serial number
    public MeterType Type { get; set; }        // Main, Individual
    public string? HouseId { get; set; }       // null = main meter
    public DateTime InstallationDate { get; set; }
}

// Oaza.Domain/Entities/MeterReading.cs
public class MeterReading
{
    public string MeterId { get; set; }        // FK to WaterMeter
    public DateTime ReadingDate { get; set; }
    public decimal Value { get; set; }         // m³ (cumulative meter state)
    public ReadingSource Source { get; set; }  // Import, Manual
    public DateTime ImportedAt { get; set; }
    public string ImportedBy { get; set; }     // FK to User
}

// Oaza.Domain/Entities/AdvancePayment.cs
public class AdvancePayment
{
    public string HouseId { get; set; }        // FK to House
    public int Year { get; set; }
    public int Month { get; set; }             // YYYY-MM
    public decimal Amount { get; set; }        // CZK
    public DateTime PaymentDate { get; set; }
}

// Oaza.Domain/Entities/Document.cs
public class Document
{
    public string Id { get; set; }
    public string Category { get; set; }       // stanovy, zapisy, smlouvy, ostatni
    public string Name { get; set; }
    public string BlobName { get; set; }       // Reference to Blob Storage
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; }
    public DateTime UploadedAt { get; set; }
    public string UploadedBy { get; set; }     // FK to User
}

// Oaza.Domain/Entities/FinancialRecord.cs
public class FinancialRecord
{
    public string Id { get; set; }
    public int Year { get; set; }
    public FinancialRecordType Type { get; set; } // Income, Expense
    public string Category { get; set; }          // voda, elektro, udrzba, pojisteni, jine
    public decimal Amount { get; set; }           // CZK
    public DateTime Date { get; set; }
    public string Description { get; set; }
    public string? AttachmentBlobName { get; set; }
}
```

### Enums

```csharp
public enum UserRole { Admin, Member, Accountant }
public enum AuthMethod { EntraId, MagicLink }
public enum MeterType { Main, Individual }
public enum ReadingSource { Import, Manual }
public enum FinancialRecordType { Income, Expense }
```

### Implementation additions (beyond the initial spec)

The code has grown past this document; the following exist in the implementation but were not in the original data model / endpoint tables:

- **`DocumentVersion`** entity + `DocumentVersions` table + repository — per-document version history (keeps the last 10 versions). PartitionKey = documentId, RowKey = zero-padded version number. Endpoints: `POST/GET /documents/{id}/versions`, `GET /documents/{id}/versions/{version}/download`.
- **Old billing model removed (X2, 27. 9. 2026).** `BillingPeriod`, `Settlement` (+ PDFs), `SupplierInvoice`, the received-invoices overview, `CalculateHouseSaldoUseCase` (`GET /advances/saldo`) and the advance pricing (water price, electricity + coefficients, common base fee, loss method) are gone — replaced by the new model below. Payments stay. Old storage tables are no longer read; nothing is migrated.
- **`AdvanceSettings`** singleton entity (PartitionKey `SETTINGS`, RowKey `advances`, table `AdvanceSettings`) — only per-house `HouseOverrides` now. `CalculatePrescribedAdvancesUseCase`: recommended advance = house's costs from `LedgerCostCollector` over `[today − 12 months, today − 1]` ÷ 12 (water + losses → water, component code prefix `ELEKTRINA` → electricity, rest → common; credits excluded; whole Kč, ≥ 0); actual = override or recommended. Endpoints: `GET/PUT /advance-settings`, `GET /advance-settings/calculate` (members only their own house).
- **Component-split payments.** `AdvancePayment` carries a 3-way split `WaterAmount`/`ElectricityAmount`/`CommonAmount` (+ `Amount`=sum), a `PaymentType` and a `Note`. `PaymentType` = `Advance` (one per house per month, RowKey `YYYY-MM`) / `Doplatek` (RowKey `D-{invertedTicks}-{guid}`) / `Payout` (`V-…`, refund, `Amount`>0) / `OpeningBalance` (`O-…`, legacy — ignored by the ledger, replaced by FundShare; UI no longer offers it). Endpoints: `POST /advances/doplatek`|`/payout`|`/opening-balance`, `DELETE /advances/{houseId}/{rowKey}`, `GET /finance/fund` (admin/accountant: Σ common contributions − Σ expenses mimo voda/elektro, via `GetFundBalanceUseCase`). UI: `web/src/pages/SaldoPage.tsx` („Platby“, `/saldo`). `FinancialRecord` supports PDF attachments: `POST`/`GET /finance/{id}/attachment` (blob container `finance`).
- **Bank statement import (Fio CSV).** `POST /bank-import/preview` (raw CSV body, ≤1 MB, saves nothing) + **stateless** `POST /bank-import/confirm` (client sends final per-row decisions; server re-validates — deliberately no `IImportSessionCache`), Admin only. `FioCsvParser` reads columns **by index** (the export has two `Poznámka` columns), UTF-8/cp1250, checks Σ against „Suma příjmů/výdajů". **Houses are matched only by counter-account**: `BankAccountMapping` (table `BankAccountMappings`, PK `MAP`, RK normalized account `{prefix-}{number}_{bank}` via `BankAccountNumber`; many accounts → one house), managed in Správa domácností (`GET/POST/DELETE /bank-accounts`) and learned on confirm. Processed movements go to `BankTransaction` (table `BankTransactions`, PK own account key, RK Fio „ID operace", `Imported`/`Ignored`) → re-uploads never duplicate. Suggestion: prescribed amount for a month without an advance → `Advance` (RowKey `YYYY-MM`), „doplat" in the message or anything else → `Doplatek`; split = prescribed or proportional (water takes the rounding remainder). Imported payments carry `BankOwnAccountKey`/`BankTransactionId` (`AdvanceResponse.IsFromBank` → „Z banky" badge); deleting such a payment also deletes its `BankTransaction`. Prescribed advances now come from `CalculatePrescribedAdvancesUseCase` (shared with `GET /advance-settings/calculate`). UI: `web/src/pages/BankImportPage.tsx` (`/advances/import`). See `docs/superpowers/specs/2026-09-27-import-bankovniho-vypisu-design.md`.
- **Reading estimates (T04).** `MeterReading.IsEstimate` + `EstimateNote` (additive; old rows = physical readings). `Oaza.Domain.Services.ReadingEstimator` — exact / linear interpolation by whole days / nearest-before / nearest-after / none, 3 decimals. `GET /readings/estimate` (Admin), UI mark `EstimateMark` (≈). Estimates are always saved with a note. Export with the flag: `GET /readings/export` (`ReadingsExportUseCase`, reuses `LedgerExport.Table`; `CubicMetres` cells = 3 decimals), button on `/readings/list`.
- **Audit log (X4). `AuditLogEntry` in table `AuditLog` (PK = UTC month `yyyy-MM`, RK = inverted ticks + id, append-only via `AddEntity`); old/new values are JSON snapshots. Every new-model use case (T02, T03, T05, T08, T09, T10) must call `IAuditLogger.LogAsync(entityType, entityId, AuditActions.*, old, new, actor, reason)` after a successful write — `Correction` requires a reason. Read: `GET /audit-log` (Admin), UI `/admin/audit`.
- **Cost components (T02).** `CostComponent` (table `CostComponents`, PK `COMPONENT`, RK guid), effective-dated `ComponentAllocationRule` and `Participation` (tables `ComponentAllocationRules`, `Participations`, PK = component id, RK guid); days stored as `yyyy-MM-dd` strings (`TableEntityMapper.ToIsoDay/GetIsoDay`). Pure logic in `Oaza.Domain/Services/AllocationSegments.cs` (segments) and `ComponentValidation.cs` (overlap, PERCENT = 100 %, closed period); orchestration + audit in `CostComponentsUseCase`; `IClosingBoundary` = last closed day (`NoClosingBoundary` until T08). Endpoints `/cost-components…` in `docs/API.md`; UI `/admin/cost-components` (`CostComponentsPage.tsx`: list, timeline, method change, participation, segments). `ApiError.details` carries every reason of a 400 `{ error, errors[] }`.
- **Opening balances (T03).** `OwnershipPeriod` (table `OwnershipPeriods`, PK house id, RK valid-from day, id `{houseId}|{yyyy-MM-dd}`) and `OpeningBalance` (table `OpeningBalances`, PK `OPENING`, RK natural key `{Type}|{target}|{periodId or -}` = uniqueness). `FundShare` sign follows X1 (positive = přeplatek) — the old `PaymentType.OpeningBalance` on the Saldo page keeps the opposite sign until T07 replaces it; don't align one to the other. A `MeterReading` opening value is also written as a `MeterReading` on that day (estimate + note). After an interim closing a change needs a reason and is audited as `Correction`. `SegmentAllocation.Split` (Domain/Services) splits an amount within one segment (EQUAL/PERCENT/static RATIO) — used by the credit preview, reuse it in T06/T07. `OpeningBalancesUseCase`, endpoints in `docs/API.md`; UI wizard `/admin/opening-balances` (`OpeningBalancesPage.tsx`, „Navrhnout z odečtů“ = T04 `GET /readings/estimate`).
- **Cost entries (T06).** `CostEntry` (table `CostEntries`, PK component id, RK guid): Advance/Settlement/OneOff, `PaidFrom` is evidence only (R6). `CostEntryAllocation.Allocate` (Domain/Services) = segments (T02) + optional cuts (`MonthStarts`) → amount pro-rata by days → `SegmentAllocation` per segment; Σ shares = amount. `RecurringAdvance.Periods` builds advance series. Metered components (water PVK): entries are invoices with `QuantityM3` (price per m³ = Σ amount ÷ Σ m³, T05), not allocated directly. New-model endpoints share `Endpoints/ModelEndpoint.cs` (JSON, errors, actor). `CostEntriesUseCase`, endpoints in `docs/API.md`; UI `/naklady` (`CostsPage.tsx`, Hospodaření → Náklady; drill-down „Rozpad na domy“).
- **Water & losses (T05).** `CostComponent.WaterRole` marks the water PVK (`Consumption`) and losses (`Losses`) components (metered, one each). `WaterSettlementCalculator` (Domain/Services): intervals between main meter readings → house consumption (boundary readings interpolated via `ReadingEstimator`, estimate flag), loss = main − Σ houses, price = PVK invoices Σ Kč ÷ Σ m³ pro-rata by days, water cost by consumption, loss cost by the losses rule per segment (EQUAL, or RATIO by consumption when `RatioSource` set); negative loss not allocated. `WaterSettlementUseCase`, `GET /water-settlement`, UI `/voda` (`WaterPage.tsx`, Hospodaření → Voda a ztráty).
- **House ledger (T07).** `Oaza.Application/Ledger/`: `LedgerCostCollector` gathers all houses' costs in a range (cost entries cut by segment, month and range bounds — only segments inside the range count; component credits; water & losses per interval starting in the range) plus a per-component control (allocated vs Σ houses, warnings). `HouseLedgerUseCase`: house ledger (FundShare opening + payments + costs, running saldo with X1 sign, ownership period default = current owner, member only own house → 403) and overview. Old `PaymentType.OpeningBalance` is ignored (replaced by FundShare). Endpoints `/ledger/...` (any signed-in user) incl. XLSX/CSV export (`LedgerExport`, `ModelEndpoint.HandleFileAsync`); UI `/saldo-domu` (`LedgerPage.tsx`).
- **Interim closings (T08).** `InterimClosing` (table `InterimClosings`, PK `CLOSING`, RK `{date}|{scope}|{house or -}`) with a JSON saldo snapshot from `HouseLedgerUseCase`. `InterimClosingBoundary` implements `IClosingBoundary.GetLastClosedDayAsync(houseId?)` (null = any closing; house = All + that house). A cost entry reaching into a closed period becomes a correction: `PostingDate` = first open day, reason required, split by the original segments; the ledger books it whole on `PostingDate`. `CostEntry.LockDate` = posting day or period start. Only the latest closing can be deleted (reason, audit). `InterimClosingsUseCase` (incl. `ExportAsync` — annual closing export for the accountant), endpoints `/interim-closings`, UI `/mezizaverky` (`InterimClosingsPage.tsx`). Contextual help sections `interimClosing` and `openingBalances` in `content/help.ts`. T12: glossary terms for every wizard step (`startUctovani`, `stavVodomeru`, `podilFondu`, `kreditSlozky`, `prevodDomu`, `mezizaverka`) shown as `HelpTerm` „?“; „Jak to funguje“ guides for the new model, the current losses method is live for Admin/Accountant (`CurrentLossMethod` in `JakToFungujePage`).
- **House transfer (T03 part C).** `HouseTransferUseCase`: preview (old owner's closing saldo via the ledger, meter suggestion via `ReadingEstimator`, blockers) and transfer = house interim closing at D−1 (`InterimClosingsUseCase`), end the old `OwnershipPeriod`, new period from D, opening FundShare (default 0) and meter reading via `OpeningBalancesUseCase`, optionally the house contact. Endpoints `/houses/{id}/transfer…`, UI section in `/admin/opening-balances` (`HouseTransferSection.tsx`).
- **Cash book (T09).** `CashBookEntry` (table `CashBook`, PK `CASH`): Deposit/Expense/Correction, amount always > 0, never edited or deleted — storno = Correction reversing its original (`CorrectionOf`). `Domain/Services/CashBook`: effects, running balance in date order, `FirstNegative` (no negative balance on any day, also back-dated). An expense with `ComponentId` creates a OneOff `CostEntry` paid from cash; its storno a negative correcting cost entry. `CashBookUseCase` (XLSX via ClosedXML, PDF via PdfSharpCore), endpoints `/cash-book` (members read; Admin + Accountant write/export); UI `/pokladna` (`CashBookPage.tsx`).
- **Invoices from documents (T11).** Documents category `faktury` (PDF/JPG/PNG only, ≤ 20 MB — `Application/Documents/DocumentUploadRules`), `Document.ComponentId` (optional link to a cost component, `?componentId=` on upload). Bulk upload in Documents (`BulkInvoiceUpload.tsx`), „Vytvořit náklad“ links to `/naklady?component=&document=` (CostsPage prefills), `GET /documents/unaccounted` (`UnaccountedDocumentsUseCase`: `faktury` documents no CostEntry.DocumentId refers to) shown on Costs as „Dokumenty bez zaúčtování“.
- **Admin guide (T12).** Page „Návod pro správce“ `/navod` (`AdminGuidePage.tsx`, Admin + Accountant, sidebar next to „Jak to funguje“); content = `web/src/content/adminGuide.ts` (`adminProcedures`: steps with exact UI labels in „…“, optional `screenshot` file in `web/public/navod/`). `e2e/adminGuide.spec.ts` walks every procedure through the UI against the mocked API, asserts the request bodies and checks it took exactly the screenshots the content lists; `GUIDE_SCREENSHOTS=1` also writes the PNGs (regenerate after UI changes: `npm run build && GUIDE_SCREENSHOTS=1 npx playwright test e2e/adminGuide.spec.ts`). Changing a step → change the spec too.
- **Off-book fund (T10, O4).** Feature flag `OFF_BOOK_FUND_ENABLED` (app setting, default off → every `/off-book-funds` endpoint 404s, `/features` tells the frontend; `FeatureFlags` built in `Program.cs`). Tables `OffBookFunds` (PK `FUND`) and `OffBookFundRecords` (PK fund id; `FundRecord.Kind` = Call/Contribution/Expense/Settlement). **Isolation:** only `Oaza.Application.OffBookFunds.OffBookFundUseCase` may depend on `IOffBookFundRepository` (a reflection test enforces it) — never read the fund from the ledger, closings, cash book or exports. UI `/fond` (`OffBookFundPage.tsx`) with a permanent warning; menu item only when the flag is on. Old-model writes respect closings too: readings (`ReadingFunctions`, `ImportReadingsUseCase` validation) are rejected up to the cut; payments (`AdvanceFunctions`, `ImportBankStatementUseCase`) use `Domain/Helpers/ClosedPeriodPayments` — edits/deletes of closed payments → 409, new late payments are booked on the first open day (an advance for a closed month becomes a doplatek) with the original date in the note.
- **Seed import (T13).** `Oaza.Application/Seed/`: CSV templates `seed/templates/` (columns: `SeedFiles`, Czech guide `seed/README.md`), fictitious S2/S3 samples `seed/samples/` (used by `SeedImportUseCaseTests`). `SeedImportUseCase` always dry-runs first over an in-memory copy (`InMemoryRepositories.cs` — the `Memory*` repos, `CopyOnAccess`; the tests use them too) through the real use cases, then reports files/issues/salda (`HouseLedgerUseCase` overview to today); apply only without errors/conflicts. Natural keys per file (house name, meter number, component code, `CostEntry.ExternalRef` = `ref`, …); an existing record with different values = conflict, never overwritten. Participations of one component go in one `CostComponentsUseCase.AddParticipationsAsync` (PERCENT sums). Endpoints `/seed-import/dry-run|report|apply` (Admin, every environment); report MD/XLSX `SeedReportExport`. UI `/admin/seed-import` (`SeedImportPage.tsx`, CSV read as UTF-8 with Windows-1250 fallback in `api/seedImport.ts`), help section `seedImport`.
- **Environment isolation (T01).** `Application/Deployment/StorageIsolation` — with `Environment` set, `AddInfrastructure` throws on another environment's storage account (prod ≠ `…dev`/`…test`/emulator; dev/test only own suffix or emulator). `EnvironmentIsolationIntegrationTests` (two Azurites; CI service `azurite2` on 2000x + env `AZURE_STORAGE_CONNECTION_2`). Provisioning: `infra/provision.sh <env> [--dry-run] [--yes]`.
- **Release (T14).** `docs/release-checklist.md` is the release procedure. Storage backup/restore of the whole account: `Oaza.Infrastructure/Backup/StorageBackup` (typed JSONL per table + blob files; restore replaces and deletes extras) + CLI `api/tools/Oaza.StorageBackup` (connection string only from env `OAZA_STORAGE_CONNECTION`, restore needs `--yes <account>`), round-trip test against Azurite. Training data `seed/demo/` (imported cleanly — `SeedImportUseCaseTests.DemoData_…`).
- **Golden fixtures (T14).** `api/tests/Oaza.Functions.Tests/Golden/GoldenFixturesTests.cs` builds the ZADANI §4 scenarios S1–S8 through the real use cases (in-memory `Memory*` repos, stepping fixed clock, `GoldenWorld` wiring), asserts the golden numbers and serializes the endpoint results with `ModelEndpoint.JsonOptions` (indented; GUIDs → `id-N`) into `web/e2e/golden/S<n>-*.json`; without `UPDATE_GOLDEN=1` a missing/different file fails. `web/e2e/golden.spec.ts` serves those files as the mocked API and drives `/voda`, `/saldo-domu`, `/admin/opening-balances`, `/pokladna`, `/fond`, `/naklady`. Changing a calculation or DTO → `UPDATE_GOLDEN=1 dotnet test Oaza.sln --filter FullyQualifiedName~Golden` and commit the JSON. `InternalsVisibleTo("Oaza.Functions.Tests")` is in `Oaza.Functions/AssemblyInfo.cs`.
- **`useApi(fetcher, deps)`** re-runs only when `deps` change — a memoized fetcher alone is not enough; pass the same array as the `useCallback` (range filters were silently not refetching before T14).
- **`User`** stores the magic-link token **hashed** (`MagicLinkTokenHash`, SHA-256) plus rate-limit/lockout counters (see above).
- **`WaterMeter.Name`** — optional display label.
- **Extra endpoints:** `DELETE /users/{id}`, `GET /readings/all`, `GET /finance/balance`, `POST`/`GET /finance/{id}/attachment`, `POST /seed` (gated by `ENABLE_SEED`).
- **Email** is Azure Communication Services (not SendGrid).

## API endpoints

All endpoints are Azure Functions HTTP triggers under `/api/`, all with `AuthorizationLevel.Anonymous` — access is enforced by `AuthenticationMiddleware` + `AuthorizationMiddleware` via `[AllowAnonymous]` / `[RequireRole(...)]` (no attribute = any authenticated user). Member "own house only" scoping is done inside each endpoint.

**The authoritative endpoint list is `docs/API.md`** (method, route, role, scoping, body shape). Update it whenever you add or change an endpoint.

Advance recommendation, payments, fund formulas plus import validation rules are documented **as implemented** in `docs/VYUCTOVANI.md` — read it before touching `CalculateSettlementUseCase`, `CloseBillingPeriodUseCase`, `CalculateHouseSaldoUseCase` or `ImportReadingsUseCase`. Its §8 lists known inconsistencies (e.g. invoice line-item vs header-month rule, `Equal` default for loss method in calculate/close).

Magic link: 15 min expiry, max 3 requests per email per hour, 5 failed verifications → token invalidated. JWT claims: `sub`, `email`, `role`, `houseId`, `authMethod`; 24h expiry.

Timer trigger (CRON `0 0 8 1 * *`): sends reading reminder on 1st of each month.

## Authentication flow

### Entra ID (primary)

1. React SPA uses MSAL.js to redirect to Entra ID login
2. After successful auth, the SPA acquires tokens silently (sessionStorage cache)
3. The **ID token** (not an access token; audience = `EntraId__ClientId`) is sent as `Authorization: Bearer {token}` to API
4. Azure Functions middleware validates the token against Entra ID OIDC metadata
5. Middleware looks up the User entity by Entra Object ID (`oid`), falling back to email, extracts role and houseId
6. If no User entity exists → 403 (must be pre-registered by admin)

### Magic link (fallback)

1. User enters email on login page → POST `/auth/magic-link`
2. API validates email exists in User table, generates GUID token, stores only its SHA-256 hash with 15min expiry
3. Azure Communication Services sends email with link: `https://oaza.cendelinovi.cz/auth/verify?token={token}&email={email}`
4. User clicks link → frontend calls POST `/auth/magic-link/verify`
5. API validates token, marks as used, returns JWT (signed with app secret, 24h expiry)
6. Frontend stores JWT in memory (not localStorage), sends as Bearer token

### RBAC middleware

Every authenticated request goes through role check:
- **Admin:** full access to everything
- **Member:** read own house data, read shared documents
- **Accountant:** read all financial data, read documents

Role is stored in User entity in Table Storage and embedded in JWT claims.

## Frontend routing

Routes are defined in `web/src/App.tsx`; `ProtectedRoute requiredRole="X"` admits role X **or Admin**. Full route table with guards: `docs/ARCHITEKTURA.md` §6. Sidebar navigation (Czech labels, role filtering) is in `web/src/components/Layout.tsx`.

### Layout

- **Desktop:** Left sidebar (200px) with navigation + main content area
- **Mobile:** Hamburger menu, full-width content
- Sidebar shows: logo, nav items (role-filtered), admin section separator, user avatar + name at bottom
- Active nav item highlighted with blue background

## Coding conventions

### Backend (.NET)

- **Naming:** PascalCase for public members, camelCase for private fields with underscore prefix (`_tableClient`)
- **Async everywhere:** All I/O operations are async, suffix with `Async`
- **Repository pattern:** `IRepository<T>` in Domain, `TableStorageRepository<T>` in Infrastructure
- **Use cases:** One class per use case in Application layer (e.g. `ImportReadingsUseCase`, `CostEntriesUseCase`)
- **DTOs:** Separate Request/Response DTOs, never expose domain entities in API
- **Validation:** FluentValidation validators per request DTO
- **Error handling:** Throw `AppException(message, statusCode)` / `NotFoundException`; each endpoint catches and writes `{ "error": … }` (validation: `{ error, errors: [{field, message}] }`). There is no global exception middleware.
- **No magic strings:** Use constants for PartitionKey values, Blob container names, claim types
- **Decimal for money:** Always use `decimal` for CZK amounts, never `double`
- **Calendar days (X5):** an accounting day is a calendar day in `Europe/Prague`. New model (T02–T10): `DateOnly` + `DateRange` (closed interval `[From, To]`, `Oaza.Domain.Time`), stored as `yyyy-MM-dd` strings; instants stay UTC. "Today" only from `IClock.Today` (`PragueClock`, in DI) — never `DateTime.UtcNow.Date` for an accounting day. Old model keeps midnight-UTC `DateTime` (`PragueClock.AsUtcMidnight`).

### Frontend (React/TypeScript)

- **Functional components only** with hooks
- **TypeScript strict mode** — no `any`, no implicit `null`
- **File naming:** PascalCase for components (`MetricCard.tsx`), camelCase for hooks (`useAuth.ts`) and utilities
- **TailwindCSS:** Utility-first, no custom CSS files except for animations
- **API calls:** Typed fetch wrapper in `api/` directory, all errors handled
- **Auth state:** React Context (`AuthContext`) wrapping the app, `useAuth()` hook
- **Date formatting:** Use `Intl.DateTimeFormat('cs-CZ')` for Czech locale
- **Today (X5):** `todayIso()` / `pragueIsoDate()` / `shiftIsoDate()` from `utils/date.ts` (Europe/Prague for every user). Never `new Date().toISOString().slice(0, 10)` — that is the UTC day.
- **Number formatting:** Use `Intl.NumberFormat('cs-CZ')` — comma as decimal separator
- **No console.log in production** — use proper error boundaries
- **Lint je CI gate (přísná React Compiler pravidla):** `npm run lint` musí projít. Pozor na `react-hooks/preserve-manual-memoization` (deps `useMemo`/`useCallback` musí přesně sedět) a `set-state-in-effect`. Build = `tsc -b && vite build` (Vite v8/rolldown).

### Git conventions

- **Branch strategy:** `develop` → DEV (`deploy-dev.yml` → `func-oaza-dev-flex`); `release/**` → TEST (`deploy-test.yml` → `func-oaza-test-flex`); `master` → PROD (`deploy.yml` → `func-oaza-prod-flex`). The old `func-oaza-{env}` (Linux Consumption, .NET 8) are stopped since 0.10.0. All three share the same Azure tenant/subscription (differ by resource group `rg-oaza-{dev,test,prod}`) and use the unified `Azure/login` + `AZURE_CREDENTIALS` + `az config-zip` deploy; the domain (`cendelinovi.cz`) and the ACS email service are shared. See `docs/DEPLOYMENT-TEST-PROD.md`.
- **Commits:** Conventional commits in English (`feat:`, `fix:`, `chore:`, `docs:`)
- **PR per implementation step** (each step = ~4h of work)
- **CHANGELOG.md:** every PR that completes a task from `docs/ZADANI.md` (status: `docs/gap-analysis.md`) adds a line under `## [Nevydáno]` (Czech, user-facing wording)
- **No force push to master**

## Azure resource naming

```
Resource Group:     rg-oaza-prod
Storage Account:    stoaza (Table Storage + Blob Storage)
  Table names:      Users, Houses, WaterMeters, MeterReadings, AdvancePayments, Documents,
                    DocumentVersions, FinancialRecords, AdvanceSettings, AuditLog,
                    CostComponents, ComponentAllocationRules, Participations,
                    OwnershipPeriods, OpeningBalances, CostEntries, InterimClosings, CashBook,
                    OffBookFunds, OffBookFundRecords,
                    BankAccountMappings, BankTransactions
  Blob containers:  documents, finance
Functions App:      func-oaza-prod-flex (Flex Consumption; old func-oaza-prod stopped)
Static Web App:     swa-oaza-prod
```

## Blob Storage structure

```
documents/
  {category}/{documentId}/{filename}        # Uploaded association documents
finance/
  {recordId}/{filename}                     # Financial record attachments
```

## Environment variables (Functions App Settings)

```
AzureWebJobsStorage=<storage-connection-string>
TableStorageConnection=<storage-connection-string>
BlobStorageConnection=<storage-connection-string>
JwtSecret=<random-256bit-key>
JwtIssuer=oaza.cendelinovi.cz
EntraId__TenantId=<entra-tenant-id>
EntraId__ClientId=<entra-app-client-id>
AzureCommunicationServices__ConnectionString=<acs-connection-string>
AzureCommunicationServices__FromEmail=DoNotReply@<acs-domain>
AzureCommunicationServices__FromName=Oáza Zadní Kopanina
AppUrl=https://oaza.cendelinovi.cz
ENABLE_SEED=true            # only on DEV/local — enables anonymous POST /api/seed
```

## Key business rules

1. **Costs are allocated, not stored per house.** A house's share is always computed from cost entries, component rules and participations (T02, T06) — never write per-house totals.

2. **Advance payments are per-month.** Regular advances use PartitionKey=houseId, RowKey=YYYY-MM (doplatek/payout/opening balance/fund use their own RowKey prefixes). The ledger books an advance on the 1st day of its month, other payments on their payment date.

3. **Loss on water network.** Difference between main meter consumption and sum of individual meters per interval between main meter readings (T05). Allocated by the losses component rule. Always show loss explicitly in UI.

4. **Excel import is two-step.** First call parses and validates (returns preview + warnings). Second call confirms and saves. Never auto-save on upload.

5. **Interim closings lock the past.** Up to the last closed day nothing may change (costs → corrections in the open period, readings/payments → 409 or booked after the cut, T08). Only the latest closing can be deleted.

6. **One user = one house** (except Admin who can see all houses).

7. **Czech number format.** Excel import must handle comma as decimal separator (e.g., `1 542,7`). UI displays numbers in Czech locale.

## Testing approach

- **Unit tests:** Domain logic (settlement calculation, loss allocation, validation rules) in `Oaza.Application.Tests`
- **Integration tests:** Table Storage repository operations in `Oaza.Infrastructure.Tests` (use Azurite local emulator)
- **E2E smoke tests (Playwright)** in `web/e2e/` against `vite preview` with the API mocked via `page.route` (`e2e/mockApi.ts`); CI job `e2e`. Scenarios S1–S8 (T14): `e2e/golden.spec.ts` on backend-generated fixtures (see Golden fixtures above).
- Build/testy přes `Oaza.sln` (NE `Oaza.slnx` — zastaralý): `dotnet test Oaza.sln` z `api/`. CI staví `--configuration Release` na .NET 10.0.x.
- Integrační testy potřebují Azurite: `docker run -d -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite`. Používají `[SkippableFact]` (skip když chybí); CI běží Azurite jako service container.

- **PR CI** (`.github/workflows/ci.yml`): warnings are errors (`api/Directory.Build.props`), coverage gate ≥ 90 % over files listed in `api/coverage-gate.txt` (add new calculation code there), web lint + build.

## Development workflow

1. Run Azurite locally for Table Storage + Blob Storage emulation
2. Run Azure Functions locally: `cd api/src/Oaza.Functions && func start`
3. Run React dev server: `cd web && npm run dev`
4. SWA CLI can proxy both together: `swa start http://localhost:5173 --api-location http://localhost:7071`

## Seed data

On first deploy (or via a seed endpoint/script), create:

- 1 admin user (Rosťa Čendelín)
- 8 houses (Zadní Kopanina 142–149)
- 9 water meters (1 main + 8 individual, each linked to a house)

## Documentation

- `README.md` (root) is the entry point; `docs/*.md` are Czech, human-facing, and describe the code **as implemented**.
- Changing a calculation → update `docs/VYUCTOVANI.md` **and** the matching texts in `web/src/content/help.ts` (its header comment maps texts to use cases).
- Adding/changing an endpoint → update `docs/API.md` (and `web/src/types/index.ts`).
- New config key → add it to `api/src/Oaza.Functions/local.settings.json.example` and `docs/LOKALNI-VYVOJ.md`.
- Never copy formulas from this file into user-facing docs — verify against code.

## Important constraints

- **No relational DB.** Azure Table Storage only. No JOINs, no foreign key enforcement. All cross-entity queries are done in application code with multiple table queries.
- **No EF Core.** Use Azure.Data.Tables SDK directly via repository pattern.
- **Consumption plan cold start.** First request after idle period may take 5–10 seconds. Acceptable for 15 users.
- **Max 20 MB file upload** for documents and invoice attachments.
- **SWA Free tier limits:** 2 custom domains, 0.5 GB storage, 100 GB bandwidth/month — more than enough.
