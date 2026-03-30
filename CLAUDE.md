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

Monorepo with two main directories:

```
oaza/
├── CLAUDE.md                     # This file
├── .github/
│   └── workflows/
│       └── deploy.yml            # GitHub Actions: build + deploy Functions + SWA
├── api/                          # .NET 8 backend
│   ├── Oaza.sln
│   ├── src/
│   │   ├── Oaza.Domain/          # Entities, value objects, interfaces, enums
│   │   ├── Oaza.Application/     # Use cases, DTOs, validators, mapping
│   │   ├── Oaza.Infrastructure/  # Table Storage repos, Blob Storage, ACS Email, JWT
│   │   └── Oaza.Functions/       # HTTP triggers, DI setup, middleware, auth
│   └── tests/
│       ├── Oaza.Domain.Tests/
│       ├── Oaza.Application.Tests/
│       └── Oaza.Functions.Tests/
└── web/                          # React 19 frontend
    ├── package.json
    ├── vite.config.ts
    ├── tailwind.config.ts
    ├── src/
    │   ├── main.tsx
    │   ├── App.tsx
    │   ├── api/                  # API client, typed fetch wrappers
    │   ├── auth/                 # AuthContext, MSAL config, magic link flow
    │   ├── components/           # Shared UI components (Layout, Sidebar, MetricCard…)
    │   ├── pages/                # Route-level page components
    │   │   ├── LoginPage.tsx
    │   │   ├── DashboardPage.tsx
    │   │   ├── ReadingsImportPage.tsx
    │   │   ├── ReadingsOverviewPage.tsx
    │   │   ├── BillingPage.tsx
    │   │   ├── DocumentsPage.tsx      # Phase 2
    │   │   ├── FinancePage.tsx         # Phase 2
    │   │   └── admin/
    │   │       ├── HousesPage.tsx
    │   │       └── UsersPage.tsx
    │   ├── hooks/                # Custom React hooks
    │   └── types/                # Shared TypeScript interfaces mirroring API DTOs
    └── staticwebapp.config.json  # SWA routing, auth config
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
- **Recharts** for charts (Phase 2)
- No state management library — React Context + hooks sufficient for 15 users

### npm packages

```json
{
  "dependencies": {
    "react": "^19",
    "react-dom": "^19",
    "react-router-dom": "^7",
    "@azure/msal-browser": "^4",
    "@azure/msal-react": "^3",
    "recharts": "^2"
  },
  "devDependencies": {
    "typescript": "^5.5",
    "vite": "^6",
    "tailwindcss": "^4",
    "@types/react": "^19"
  }
}
```

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
    public string? MagicLinkToken { get; set; }
    public DateTime? MagicLinkExpiry { get; set; }
    public DateTime? LastLogin { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
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

## API endpoints

All endpoints are Azure Functions HTTP triggers. Base path: `/api/`.

### Authentication

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/auth/magic-link` | Public | Request magic link — validates email, generates token (GUID), stores in User entity with 15min expiry, sends via Azure Communication Services |
| POST | `/auth/magic-link/verify` | Public | Verify magic link token — validates token + expiry + one-time use, returns JWT |
| GET | `/auth/me` | Authenticated | Returns current user profile (from JWT claims + User entity) |

Magic link rate limit: max 3 requests per email per hour.

JWT includes claims: `sub` (user ID), `email`, `role`, `houseId`, `authMethod`.

Entra ID auth is handled by Static Web Apps built-in integration — the Functions receive a validated JWT from SWA proxy with Entra claims mapped to our User entity.

### Houses & meters (Admin only for write)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/houses` | Authenticated | List all houses |
| GET | `/houses/{id}` | Authenticated | Get house detail |
| POST | `/houses` | Admin | Create house |
| PUT | `/houses/{id}` | Admin | Update house |
| GET | `/meters` | Authenticated | List all meters |
| POST | `/meters` | Admin | Create meter |
| PUT | `/meters/{id}` | Admin | Update meter |

### Users (Admin only)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/users` | Admin | List all users |
| POST | `/users` | Admin | Create user (invite) |
| PUT | `/users/{id}` | Admin | Update user (role, house assignment) |

### Meter readings

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/readings/import` | Admin | Upload .xlsx, parse & validate, return preview + warnings (does NOT save yet) |
| POST | `/readings/import/confirm` | Admin | Confirm import — saves validated readings to Table Storage |
| GET | `/readings?year=&month=` | Authenticated | Get readings for month — admin sees all, member sees own house |
| POST | `/readings` | Admin | Manual single reading entry |
| PUT | `/readings/{meterId}/{date}` | Admin | Correct a reading |
| GET | `/readings/chart?houseId=&from=&to=` | Authenticated | Chart data — monthly consumption over time (Phase 2) |

#### Excel import validation rules

1. **Duplicate check:** reading already exists for same meter + same month → error
2. **Negative consumption:** new reading value < previous reading value → error
3. **Completeness:** all configured meters must have a value → warning if missing
4. **Anomaly detection:** consumption > 2× rolling average → warning (not blocking)
5. **Czech number format:** comma as decimal separator must be handled

### Supplier invoices

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/invoices?year=` | Admin, Accountant | List invoices |
| POST | `/invoices` | Admin | Create invoice (with optional attachment upload to Blob) |
| PUT | `/invoices/{id}` | Admin | Update invoice |
| DELETE | `/invoices/{id}` | Admin | Delete invoice |

### Advance payments

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/advances?houseId=&year=` | Authenticated | List advances — member sees own house only |
| POST | `/advances` | Admin | Record advance payment |
| PUT | `/advances/{houseId}/{yearMonth}` | Admin | Update advance |

### Billing periods & settlements

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/billing-periods` | Authenticated | List all periods |
| POST | `/billing-periods` | Admin | Create new period (dateFrom, dateTo) |
| GET | `/billing-periods/{id}/calculate` | Admin | Calculate settlement — returns preview (does NOT save) |
| POST | `/billing-periods/{id}/close` | Admin | Close period — saves Settlement entities, locks period |
| GET | `/billing-periods/{id}/settlements` | Authenticated | Get settlements for period — member sees own house only |
| GET | `/billing-periods/{id}/settlements/{houseId}/pdf` | Authenticated | Download PDF settlement sheet for one house |
| GET | `/billing-periods/{id}/pdf` | Admin | Download ZIP with all PDF sheets |

#### Settlement calculation logic

```
1. Main meter consumption = endReading - startReading (for period date range)
2. For each house: houseConsumption = endReading - startReading (per house meter)
3. Loss = mainMeterConsumption - SUM(allHouseConsumptions)
4. Loss allocation (configurable):
   a) Equal: loss / numberOfHouses
   b) Proportional: loss × (houseConsumption / totalHouseConsumption)
5. Each house's share = (houseConsumption + allocatedLoss) / (totalHouseConsumption + totalLoss)
6. Each house's amount = share × SUM(SupplierInvoice.Amount for period)
7. Each house's advances = SUM(AdvancePayment.Amount for house within period dates)
8. Balance = amount - advances (positive = underpayment/doplatek, negative = overpayment/přeplatek)
```

### Documents (Phase 2)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/documents?category=` | Authenticated | List documents, filter by category |
| POST | `/documents` | Admin | Upload document (multipart: file + metadata) |
| GET | `/documents/{id}/download` | Authenticated | Get SAS URL for download |
| DELETE | `/documents/{id}` | Admin | Delete document |

### Financial records (Phase 2)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/finance?year=&category=` | Authenticated | List records |
| GET | `/finance/summary?year=` | Authenticated | Aggregated summary by category |
| POST | `/finance` | Admin | Create record |
| PUT | `/finance/{id}` | Admin | Update record |
| GET | `/finance/export/pdf?year=` | Admin, Accountant | Export annual PDF report |
| GET | `/finance/export/xlsx?year=` | Admin, Accountant | Export annual Excel report |

### Notifications (Phase 2)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/notifications/send` | Admin | Manual notification send |

Timer trigger (CRON `0 0 8 1 * *`): sends reading reminder on 1st of each month.

## Authentication flow

### Entra ID (primary)

1. React SPA uses MSAL.js to redirect to Entra ID login
2. After successful auth, MSAL returns an access token
3. Token is sent as `Authorization: Bearer {token}` to API
4. Azure Functions middleware validates the token against Entra ID OIDC metadata
5. Middleware looks up the User entity by Entra Object ID, extracts role and houseId
6. If no User entity exists → 403 (must be pre-registered by admin)

### Magic link (fallback)

1. User enters email on login page → POST `/auth/magic-link`
2. API validates email exists in User table, generates GUID token, stores with 15min expiry
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

```typescript
// React Router v7 routes
/login                    → LoginPage (public)
/auth/verify              → MagicLinkVerifyPage (public)
/dashboard                → DashboardPage (role-dependent view)
/readings                 → ReadingsOverviewPage (admin: all, member: own house)
/readings/import          → ReadingsImportPage (admin only)
/billing                  → BillingPage (admin: manage, member: view own)
/documents                → DocumentsPage (Phase 2)
/finance                  → FinancePage (Phase 2)
/admin/houses             → HousesPage (admin only)
/admin/users              → UsersPage (admin only)
```

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
- **Error handling:** Custom `AppException` with HTTP status codes, global exception handler in Functions middleware
- **No magic strings:** Use constants for PartitionKey values, Blob container names, claim types
- **Decimal for money:** Always use `decimal` for CZK amounts, never `double`

### Frontend (React/TypeScript)

- **Functional components only** with hooks
- **TypeScript strict mode** — no `any`, no implicit `null`
- **File naming:** PascalCase for components (`MetricCard.tsx`), camelCase for hooks (`useAuth.ts`) and utilities
- **TailwindCSS:** Utility-first, no custom CSS files except for animations
- **API calls:** Typed fetch wrapper in `api/` directory, all errors handled
- **Auth state:** React Context (`AuthContext`) wrapping the app, `useAuth()` hook
- **Date formatting:** Use `Intl.DateTimeFormat('cs-CZ')` for Czech locale
- **Number formatting:** Use `Intl.NumberFormat('cs-CZ')` — comma as decimal separator
- **No console.log in production** — use proper error boundaries

### Git conventions

- **Branch strategy:** `main` = production (auto-deploy)
- **Commits:** Conventional commits in English (`feat:`, `fix:`, `chore:`, `docs:`)
- **PR per implementation step** (each step = ~4h of work)
- **No force push to main**

## Azure resource naming

```
Resource Group:     rg-oaza-prod
Storage Account:    stoaza (Table Storage + Blob Storage)
  Table names:      Users, Houses, WaterMeters, MeterReadings, BillingPeriods,
                    SupplierInvoices, AdvancePayments, Settlements, Documents, FinancialRecords
  Blob containers:  documents, invoices, settlements
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
```

## Key business rules

1. **BillingPeriod total is computed, not stored.** Always SUM(SupplierInvoice.Amount) where invoice month falls within period dateFrom–dateTo. Never write a total into BillingPeriod entity.

2. **Advance payments are per-month, not per-period.** AdvancePayment uses PartitionKey=houseId, RowKey=YYYY-MM. At settlement time, SUM all payments where YYYY-MM falls within the billing period.

3. **Loss on water network.** Difference between main meter consumption and sum of individual meters. Must be allocated to houses — configurable method (equal split or proportional to consumption). Always show loss explicitly in UI.

4. **Excel import is two-step.** First call parses and validates (returns preview + warnings). Second call confirms and saves. Never auto-save on upload.

5. **Closing a billing period is irreversible.** Once closed, Settlement entities are written and the period is locked. Readings and invoices within the period can no longer be modified.

6. **One user = one house** (except Admin who can see all houses).

7. **Czech number format.** Excel import must handle comma as decimal separator (e.g., `1 542,7`). UI displays numbers in Czech locale.

## Testing approach

- **Unit tests:** Domain logic (settlement calculation, loss allocation, validation rules) in `Oaza.Application.Tests`
- **Integration tests:** Table Storage repository operations in `Oaza.Infrastructure.Tests` (use Azurite local emulator)
- **No E2E automation** — manual E2E testing (15 users, not worth the investment)
- Run tests: `dotnet test` from `api/` directory

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

## Important constraints

- **No relational DB.** Azure Table Storage only. No JOINs, no foreign key enforcement. All cross-entity queries are done in application code with multiple table queries.
- **No EF Core.** Use Azure.Data.Tables SDK directly via repository pattern.
- **Consumption plan cold start.** First request after idle period may take 5–10 seconds. Acceptable for 15 users.
- **Max 20 MB file upload** for documents and invoice attachments.
- **SWA Free tier limits:** 2 custom domains, 0.5 GB storage, 100 GB bandwidth/month — more than enough.
