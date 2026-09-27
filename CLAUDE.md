# CLAUDE.md — Oáza Zadní Kopanina Portal

## Project overview

Community portal for a small neighborhood association ("Oáza Zadní Kopanina") in Prague. 8 households, up to 15 users. Primary function: shared water supply management — monthly meter readings, billing settlements, advance payments. Secondary: shared document storage and basic financial overview.

**Domain:** `oaza.cendelinovi.cz`
**Operator:** Single-person ops (Rosťa Čendelín)
**Language:** Czech UI, English code (variable names, comments, commit messages)

## Architecture

Three-layer Clean Architecture on Azure, cost-optimized for ~5–15 CZK/month:

```
React 19 SPA (Azure Static Web Apps, Free)
    ↕ HTTPS / REST API
.NET 8 Azure Functions (Consumption plan, Isolated Worker)
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
├── api/                          # .NET 8 backend
│   ├── Oaza.sln                  # use this (Oaza.slnx is stale)
│   ├── global.json               # pins .NET 8 SDK
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

- **.NET 8** with Azure Functions Isolated Worker model
- **Azure.Data.Tables** SDK for Table Storage (NOT EF Core — no relational DB)
- **ClosedXML** for Excel import/export (.xlsx parsing)
- **PdfSharpCore** for PDF generation (settlement sheets)
- **Azure Communication Services** for magic link emails and notifications
- **FluentValidation** for request validation
- **System.IdentityModel.Tokens.Jwt** for JWT generation/validation

### NuGet packages

**Verze pinuj explicitně — žádné floating wildcardy (`12.*`).** Floaty driftnou na `Azure.Core 1.55` (vyžaduje `Microsoft.Extensions.* >=10`) a rozbijou build proti pinům `8.*`. Verze jsou nyní pevné a `global.json` zamyká SDK na .NET 8 (`rollForward: latestFeature`).

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
| BillingPeriod | `PERIOD` | GUID | All periods in one partition |
| SupplierInvoice | `INVOICE` | GUID | All invoices in one partition, filter by date in app layer |
| AdvancePayment | house GUID | `YYYY-MM` (e.g. `2026-03`) | Query all payments for a house, filter by date range for billing |
| Settlement | period GUID | house GUID | All settlements for a period in one partition |
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

// Oaza.Domain/Entities/BillingPeriod.cs
public class BillingPeriod
{
    public string Id { get; set; }
    public string Name { get; set; }           // e.g. "2. pololetí 2025"
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public BillingPeriodStatus Status { get; set; } // Open, Closed
    // Total invoice amount is NOT stored — computed as SUM(SupplierInvoice.Amount) for this period
}

// Oaza.Domain/Entities/SupplierInvoice.cs
public class SupplierInvoice
{
    public string Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }             // Invoice period (YYYY-MM)
    public string InvoiceNumber { get; set; }
    public DateTime IssuedDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }        // CZK
    public decimal ConsumptionM3 { get; set; } // m³ per invoice
    public string? AttachmentBlobName { get; set; }
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

// Oaza.Domain/Entities/Settlement.cs
public class Settlement
{
    public string PeriodId { get; set; }       // FK to BillingPeriod
    public string HouseId { get; set; }        // FK to House
    public decimal ConsumptionM3 { get; set; }
    public decimal SharePercent { get; set; }
    public decimal CalculatedAmount { get; set; }  // CZK
    public decimal TotalAdvances { get; set; }     // CZK
    public decimal Balance { get; set; }           // Negative = overpayment, positive = underpayment
    public decimal LossAllocatedM3 { get; set; }   // Loss allocated to this house
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
public enum BillingPeriodStatus { Open, Closed }
public enum FinancialRecordType { Income, Expense }
public enum LossAllocationMethod { Equal, ProportionalToConsumption }
```

### Implementation additions (beyond the initial spec)

The code has grown past this document; the following exist in the implementation but were not in the original data model / endpoint tables:

- **`DocumentVersion`** entity + `DocumentVersions` table + repository — per-document version history (keeps the last 10 versions). PartitionKey = documentId, RowKey = zero-padded version number. Endpoints: `POST/GET /documents/{id}/versions`, `GET /documents/{id}/versions/{version}/download`.
- **`AdvanceSettings`** singleton entity (PartitionKey `SETTINGS`, RowKey `advances`, table `AdvanceSettings`) — advance-payment pricing: water price/validity, monthly electricity cost + per-house coefficients, common base fee, per-house overrides, and a `LossAllocationMethod`. Endpoints: `GET/PUT /advance-settings`, `GET /advance-settings/calculate` (admins/accountants see all houses; members see only their own). Loaded via `IAdvanceSettingsRepository`.
- **Component-split payments + per-house saldo.** `AdvancePayment` carries a 3-way split `WaterAmount`/`ElectricityAmount`/`CommonAmount` (+ `Amount`=sum), a `PaymentType` and a `Note`. `PaymentType` = `Advance`/`Doplatek` (component-split, fed into the water settlement) + `Payout`/`OpeningBalance` (net-level, NEVER seen by settlement — `GetByHouseAndPeriodAsync` filters to Advance+Doplatek). A **doplatek** is ad-hoc/repeatable (RowKey `D-{invertedTicks}-{guid}`); a regular advance is one-per-house-per-month (`YYYY-MM`); **payout** (`V-…`, refund of přeplatek, `Amount`>0) and **opening balance** (`O-…`, one-time seed, SIGNED `Amount`: +=nedoplatek, −=přeplatek) are net-level. `Settlement` extended with `ElectricityCharge`/`ElectricityAdvances`/`CommonCharge`/`CommonAdvances`; `TotalAdvances` is now WATER advances only.
  - **Saldo: one net balance per house is the spine** (`TotalSaldo = ComponentSaldo + NetAdjustments`; +=nedoplatek, −=přeplatek); per-component voda/elektřina/společný is an analytical breakdown (money is fungible). Period-based: charges locked per period (closed=snapshot, open=live preview), paid recomputed live (post-close doplatky/payouts count); payments outside any period = "Nezařazené platby". `House.DissolveOverpayment` flag + "vystačí ~N měsíců" = |přeplatek| ÷ prescribed monthly (override sum). Endpoints: `POST /advances/doplatek`|`/payout`|`/opening-balance`, `DELETE /advances/{houseId}/{rowKey}`, `GET /advances/saldo`, `GET /finance/fund` (admin/accountant: Σ common contributions − Σ expenses mimo voda/elektro, via `GetFundBalanceUseCase`). UI: `web/src/pages/SaldoPage.tsx` (`/saldo`). See `CalculateHouseSaldoUseCase`.
- **Fund draw + water-price carry-forward at settlement close.** `POST /billing-periods/{id}/close` optionally accepts `fundDrawAmount` (drawn from the common fund, split evenly across active houses as one auto-generated doplatek each — `AdvancePayment.IsFundTransfer=true`, deterministic `RowKey = "FUND-{periodId}"` so a retry overwrites instead of duplicating — plus one `FinancialRecord` expense, category `fond-voda`, deterministic `Id = "fund-{periodId}"`) and `applyNewWaterPrice`/`newWaterPriceValidFrom` (carries the period's realized invoice price/m³, incl. loss, forward into `AdvanceSettings.WaterPricePerM3`/`WaterPriceValidFrom`). Both are computed live in the frontend preview from data the existing `/calculate` + `/finance/fund` + `/advance-settings` endpoints already return — no new preview endpoint. See `CloseBillingPeriodUseCase.ApplyFundDrawAsync`/`ApplyNewWaterPriceAsync` and `docs/superpowers/specs/2026-07-18-vodni-fond-a-cena-design.md`.
- **Received-invoices overview + FinancialRecord attachments.** A read-only unified view of all received invoices — water `SupplierInvoice` **plus** expense `FinancialRecord`s — via `GET /invoices/all?year=&category=` (Admin/Accountant), backed by `GetReceivedInvoicesUseCase` which joins both tables into `ReceivedInvoiceResponse` (`Source` `voda`/`ostatni`, `CountsTowardWaterSettlement`, `AttachmentDownloadPath`). The two entities stay **structurally separate** — the join is read-only and never feeds settlement, keeping the "BillingPeriod total = SUM(SupplierInvoice.Amount)" rule intact. `FinancialRecord` now supports PDF attachments: `POST`/`GET /finance/{id}/attachment` (blob container `finance`). Invoice/overview year filtering matches on **`IssuedDate.Year`** (document date), and both the overview and the "Faktury za vodu" list derive their year options from real data, so a multi-year invoice is always findable (`SupplierInvoiceRepository.GetByYearAsync` filters by `IssuedDate.Year`; settlement is unaffected — it matches per line-item `DateFrom`). UI: `web/src/pages/InvoicesOverviewPage.tsx` (`/prehled-faktur`, Admin/Accountant). See `docs/superpowers/specs/2026-07-18-prehled-faktur-design.md`.
- **Bank statement import (Fio CSV).** `POST /bank-import/preview` (raw CSV body, ≤1 MB, saves nothing) + **stateless** `POST /bank-import/confirm` (client sends final per-row decisions; server re-validates — deliberately no `IImportSessionCache`), Admin only. `FioCsvParser` reads columns **by index** (the export has two `Poznámka` columns), UTF-8/cp1250, checks Σ against „Suma příjmů/výdajů". **Houses are matched only by counter-account**: `BankAccountMapping` (table `BankAccountMappings`, PK `MAP`, RK normalized account `{prefix-}{number}_{bank}` via `BankAccountNumber`; many accounts → one house), managed in Správa domácností (`GET/POST/DELETE /bank-accounts`) and learned on confirm. Processed movements go to `BankTransaction` (table `BankTransactions`, PK own account key, RK Fio „ID operace", `Imported`/`Ignored`) → re-uploads never duplicate. Suggestion: prescribed amount for a month without an advance → `Advance` (RowKey `YYYY-MM`), „doplat" in the message or anything else → `Doplatek`; split = prescribed or proportional (water takes the rounding remainder). Imported payments carry `BankOwnAccountKey`/`BankTransactionId` (`AdvanceResponse.IsFromBank` → „Z banky" badge); deleting such a payment also deletes its `BankTransaction`. Prescribed advances now come from `CalculatePrescribedAdvancesUseCase` (shared with `GET /advance-settings/calculate`). UI: `web/src/pages/BankImportPage.tsx` (`/advances/import`). See `docs/superpowers/specs/2026-09-27-import-bankovniho-vypisu-design.md`.
- **Reading estimates (T04).** `MeterReading.IsEstimate` + `EstimateNote` (additive; old rows = physical readings). `Oaza.Domain.Services.ReadingEstimator` — exact / linear interpolation by whole days / nearest-before / nearest-after / none, 3 decimals. `GET /readings/estimate` (Admin), UI mark `EstimateMark` (≈). Estimates are always saved with a note.
- **Audit log (X4). `AuditLogEntry` in table `AuditLog` (PK = UTC month `yyyy-MM`, RK = inverted ticks + id, append-only via `AddEntity`); old/new values are JSON snapshots. Every new-model use case (T02, T03, T05, T08, T09, T10) must call `IAuditLogger.LogAsync(entityType, entityId, AuditActions.*, old, new, actor, reason)` after a successful write — `Correction` requires a reason. Read: `GET /audit-log` (Admin), UI `/admin/audit`.
- **Cost components (T02).** `CostComponent` (table `CostComponents`, PK `COMPONENT`, RK guid), effective-dated `ComponentAllocationRule` and `Participation` (tables `ComponentAllocationRules`, `Participations`, PK = component id, RK guid); days stored as `yyyy-MM-dd` strings (`TableEntityMapper.ToIsoDay/GetIsoDay`). Pure logic in `Oaza.Domain/Services/AllocationSegments.cs` (segments) and `ComponentValidation.cs` (overlap, PERCENT = 100 %, closed period); orchestration + audit in `CostComponentsUseCase`; `IClosingBoundary` = last closed day (`NoClosingBoundary` until T08). Endpoints `/cost-components…` in `docs/API.md`.
- **`User`** stores the magic-link token **hashed** (`MagicLinkTokenHash`, SHA-256) plus rate-limit/lockout counters (see above).
- **`WaterMeter.Name`** — optional display label.
- **Extra endpoints:** `DELETE /users/{id}`, `GET /readings/all`, `GET /finance/balance`, `GET /invoices/all`, `POST`/`GET /finance/{id}/attachment`, `POST /seed` (gated by `ENABLE_SEED`).
- **Email** is Azure Communication Services (not SendGrid).

## API endpoints

All endpoints are Azure Functions HTTP triggers under `/api/`, all with `AuthorizationLevel.Anonymous` — access is enforced by `AuthenticationMiddleware` + `AuthorizationMiddleware` via `[AllowAnonymous]` / `[RequireRole(...)]` (no attribute = any authenticated user). Member "own house only" scoping is done inside each endpoint.

**The authoritative endpoint list is `docs/API.md`** (method, route, role, scoping, body shape). Update it whenever you add or change an endpoint.

Settlement, close-period, saldo, advance and fund formulas plus import validation rules are documented **as implemented** in `docs/VYUCTOVANI.md` — read it before touching `CalculateSettlementUseCase`, `CloseBillingPeriodUseCase`, `CalculateHouseSaldoUseCase` or `ImportReadingsUseCase`. Its §8 lists known inconsistencies (e.g. invoice line-item vs header-month rule, `Equal` default for loss method in calculate/close).

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
- **Use cases:** One class per use case in Application layer (e.g. `ImportReadingsUseCase`, `CalculateSettlementUseCase`)
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

- **Branch strategy:** `develop` → DEV (`deploy-dev.yml` → `func-oaza-dev`); `release/**` → TEST (`deploy-test.yml` → `func-oaza-test`); `master` → PROD (`deploy.yml` → `func-oaza-prod`). All three share the same Azure tenant/subscription (differ by resource group `rg-oaza-{dev,test,prod}`) and use the unified `Azure/login` + `AZURE_CREDENTIALS` + `az config-zip` deploy; the domain (`cendelinovi.cz`) and the ACS email service are shared. See `docs/DEPLOYMENT-TEST-PROD.md`.
- **Commits:** Conventional commits in English (`feat:`, `fix:`, `chore:`, `docs:`)
- **PR per implementation step** (each step = ~4h of work)
- **CHANGELOG.md:** every PR that completes a task from `docs/ZADANI.md` (status: `docs/gap-analysis.md`) adds a line under `## [Nevydáno]` (Czech, user-facing wording)
- **No force push to master**

## Azure resource naming

```
Resource Group:     rg-oaza-prod
Storage Account:    stoaza (Table Storage + Blob Storage)
  Table names:      Users, Houses, WaterMeters, MeterReadings, BillingPeriods,
                    SupplierInvoices, AdvancePayments, Settlements, Documents,
                    DocumentVersions, FinancialRecords, AdvanceSettings, AuditLog,
                    CostComponents, ComponentAllocationRules, Participations,
                    BankAccountMappings, BankTransactions
  Blob containers:  documents, invoices, settlements, finance
Functions App:      func-oaza-prod
Static Web App:     swa-oaza-prod
```

## Blob Storage structure

```
documents/
  {category}/{documentId}/{filename}        # Uploaded association documents
invoices/
  {invoiceId}/{filename}                    # Supplier invoice attachments
settlements/
  {periodId}/{houseId}.pdf                  # Generated settlement PDFs
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

1. **BillingPeriod total is computed, not stored.** Settlement sums invoice line items whose `DateFrom` falls within the period (× (1 + VAT)); legacy invoices without line items fall back to header Year/Month + `Amount`. Never write a total into BillingPeriod entity. (Some list/PDF totals still use the header-month rule — see `docs/VYUCTOVANI.md` §8.)

2. **Advance payments are per-month, not per-period.** Regular advances use PartitionKey=houseId, RowKey=YYYY-MM (doplatek/payout/opening balance/fund use their own RowKey prefixes). At settlement time, SUM water components of Advance+Doplatek whose effective date falls within the billing period.

3. **Loss on water network.** Difference between main meter consumption and sum of individual meters. Must be allocated to houses — configurable method (equal split or proportional to consumption). Always show loss explicitly in UI.

4. **Excel import is two-step.** First call parses and validates (returns preview + warnings). Second call confirms and saves. Never auto-save on upload.

5. **Closing a billing period is irreversible.** Once closed, Settlement entities are written and the period is locked. Readings and invoices within the period can no longer be modified. (Intended behaviour — the lock currently has gaps, e.g. reading import/manual create/date-move; see `docs/VYUCTOVANI.md` §3 and §8.)

6. **One user = one house** (except Admin who can see all houses).

7. **Czech number format.** Excel import must handle comma as decimal separator (e.g., `1 542,7`). UI displays numbers in Czech locale.

## Testing approach

- **Unit tests:** Domain logic (settlement calculation, loss allocation, validation rules) in `Oaza.Application.Tests`
- **Integration tests:** Table Storage repository operations in `Oaza.Infrastructure.Tests` (use Azurite local emulator)
- **E2E smoke tests (Playwright)** in `web/e2e/` against `vite preview` with the API mocked via `page.route` (`e2e/mockApi.ts`); CI job `e2e`. Full scenario suite S1–S8 comes with T14.
- Build/testy přes `Oaza.sln` (NE `Oaza.slnx` — zastaralý): `dotnet test Oaza.sln` z `api/`. CI staví `--configuration Release` na .NET 8.0.x.
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
