# Přehled přijatých faktur — implementační plán

> **Spec:** `docs/superpowers/specs/2026-07-18-prehled-faktur-design.md`
> **Cíl:** Read-only přehled všech přijatých faktur (voda + ostatní výdaje), přílohy k FinancialRecord, sjednocený filtr roku podle data vystavení.
> **Tech:** .NET 8 Azure Functions + Azure.Data.Tables (backend), React 19 + Vite + Tailwind (frontend). TDD, časté commity.

Postup: backend (Tasks 1–4) → frontend (Tasks 5–8) → dokumentace (Task 9). Po každém tasku build + testy zelené, commit.

---

## Task 1: `ReceivedInvoiceResponse` DTO + `GetReceivedInvoicesUseCase`

**Files:**
- Create: `api/src/Oaza.Application/DTOs/ReceivedInvoiceResponse.cs`
- Create: `api/src/Oaza.Application/UseCases/GetReceivedInvoicesUseCase.cs`
- Test: `api/tests/Oaza.Application.Tests/UseCases/GetReceivedInvoicesUseCaseTests.cs`

**DTO:**
```csharp
namespace Oaza.Application.DTOs;

public record ReceivedInvoiceResponse(
    string Source,        // "voda" | "ostatni"
    string Id,
    DateTime Date,        // IssuedDate (voda) | Date (ostatni)
    string Category,      // "voda" | FinancialRecord.Category
    string Description,   // InvoiceNumber (voda) | Description (ostatni)
    decimal Amount,
    DateTime? DueDate,
    bool CountsTowardWaterSettlement,
    bool HasAttachment,
    string? AttachmentDownloadPath);
```

**Use case** (po vzoru `GetFundBalanceUseCase`, čistě READ):
```csharp
public class GetReceivedInvoicesUseCase
{
    private readonly ISupplierInvoiceRepository _invoiceRepository;
    private readonly IFinancialRecordRepository _financialRecordRepository;

    public GetReceivedInvoicesUseCase(
        ISupplierInvoiceRepository invoiceRepository,
        IFinancialRecordRepository financialRecordRepository) { ... }

    // year == null → všechny roky; category == null/"" → obě skupiny;
    // "voda" → jen SupplierInvoice; jinak → jen FinancialRecord dané kategorie.
    public async Task<IReadOnlyList<ReceivedInvoiceResponse>> GetAsync(int? year, string? category)
    {
        var result = new List<ReceivedInvoiceResponse>();
        var wantWater = string.IsNullOrEmpty(category) || category.Equals("voda", StringComparison.OrdinalIgnoreCase);
        var wantOther = string.IsNullOrEmpty(category) || !category.Equals("voda", StringComparison.OrdinalIgnoreCase);

        if (wantWater)
        {
            var invoices = await _invoiceRepository.GetByPartitionKeyAsync(PartitionKeys.Invoice);
            foreach (var inv in invoices.Where(i => year == null || i.IssuedDate.Year == year))
                result.Add(new ReceivedInvoiceResponse(
                    "voda", inv.Id, inv.IssuedDate, "voda", inv.InvoiceNumber, inv.Amount,
                    inv.DueDate, true, !string.IsNullOrEmpty(inv.AttachmentBlobName),
                    string.IsNullOrEmpty(inv.AttachmentBlobName) ? null : $"/invoices/{inv.Id}/attachment"));
        }

        if (wantOther)
        {
            var records = await _financialRecordRepository.GetAllAsync();
            foreach (var r in records.Where(r =>
                r.Type == FinancialRecordType.Expense
                && (year == null || r.Date.Year == year)
                && (string.IsNullOrEmpty(category) || category.Equals("voda", StringComparison.OrdinalIgnoreCase) == false && r.Category.Equals(category, StringComparison.OrdinalIgnoreCase))))
                result.Add(new ReceivedInvoiceResponse(
                    "ostatni", r.Id, r.Date, r.Category, r.Description, r.Amount,
                    null, false, !string.IsNullOrEmpty(r.AttachmentBlobName),
                    string.IsNullOrEmpty(r.AttachmentBlobName) ? null : $"/finance/{r.Id}/attachment"));
        }

        return result.OrderByDescending(x => x.Date).ToList();
    }
}
```
> Poznámka: `category="voda"` filtr platí jen pro `wantOther=false`, takže vodní filtr nikdy nechytá FinancialRecord kategorie "voda" — přehled bere vodu výhradně ze SupplierInvoice (viz spec sekce 9). Pro jinou kategorii se vrací jen FinancialRecordy té kategorie.

**Testy** (mock repozitáře, po vzoru `GetFundBalanceUseCaseTests`):
- `GetAsync(null, null)` → spojí vodu i ostatní, řazeno podle `Date` sestupně.
- filtr roku: SupplierInvoice podle `IssuedDate.Year`, FinancialRecord podle `Date.Year`.
- `category="voda"` → jen `Source="voda"`; `category="elektro"` → jen ostatní té kategorie.
- Income FinancialRecord se ignoruje (jen Expense).
- `CountsTowardWaterSettlement`, `HasAttachment`, `AttachmentDownloadPath` správně namapované.

**Kroky:** napsat test (fail) → build/test fail → implementovat DTO+use case → test pass → celý `dotnet test` → commit `feat(api): GetReceivedInvoicesUseCase joining water invoices and expense records`.

---

## Task 2: Endpoint `GET /invoices/all`

**Files:**
- Modify: `api/src/Oaza.Functions/Endpoints/InvoiceFunctions.cs` (nová funkce + konstruktor dep `GetReceivedInvoicesUseCase`)
- Modify: `api/src/Oaza.Functions/Program.cs` (DI registrace use case)

**Endpoint** (v `InvoiceFunctions`, přidat dep `GetReceivedInvoicesUseCase _receivedInvoicesUseCase`):
```csharp
[Function("GetReceivedInvoices")]
[RequireRole(UserRole.Admin, UserRole.Accountant)]
public async Task<HttpResponseData> GetReceivedInvoicesAsync(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "invoices/all")] HttpRequestData req)
{
    try
    {
        var qp = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        int? year = int.TryParse(qp["year"], out var y) ? y : null;
        var category = qp["category"];
        var result = await _receivedInvoicesUseCase.GetAsync(year, category);
        return await WriteJsonResponseAsync(req, HttpStatusCode.OK, result);
    }
    catch (AppException ex) { return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message); }
    catch (Exception) { return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba."); }
}
```
> ROUTE POZOR: `invoices/all` musí být zaregistrováno tak, aby nekolidovalo s `invoices/{id}`. Functions HTTP routing bere `all` jako literál před parametrem `{id}` — literální segment má přednost, takže OK. Ověřit při běhu.

**Program.cs** — přidat vedle `GetFundBalanceUseCase`:
```csharp
services.AddSingleton<GetReceivedInvoicesUseCase>(sp =>
    new GetReceivedInvoicesUseCase(
        sp.GetRequiredService<ISupplierInvoiceRepository>(),
        sp.GetRequiredService<IFinancialRecordRepository>()));
```

**Kroky:** implementovat → `dotnet build` → celý `dotnet test` (žádná regrese) → commit `feat(api): GET /invoices/all endpoint for received-invoices overview`.

---

## Task 3: Přílohy k FinancialRecord (upload/download)

**Files:**
- Modify: `api/src/Oaza.Functions/Endpoints/FinanceFunctions.cs` (2 nové funkce + dep `IBlobStorageService`)
- Modify: `api/src/Oaza.Functions/Program.cs` — pokud FinanceFunctions není konstrukčně injektovaná (Functions třídy se instancují DI automaticky; `IBlobStorageService` už je registrovaná pro InvoiceFunctions, takže stačí přidat parametr do konstruktoru).

Přidat do `FinanceFunctions` konstruktoru `IBlobStorageService _blobStorageService` (+ `const long MaxAttachmentBytes = 20*1024*1024`).

```csharp
[Function("UploadFinanceAttachment")]
[RequireRole(UserRole.Admin)]
public async Task<HttpResponseData> UploadFinanceAttachmentAsync(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "finance/{id}/attachment")] HttpRequestData req,
    string id)
{
    try
    {
        var existing = await FindFinancialRecordByIdAsync(id);
        if (existing is null) throw new NotFoundException("FinancialRecord", id);

        var contentType = req.Headers.TryGetValues("Content-Type", out var ct) ? ct.FirstOrDefault() ?? "" : "";
        if (!contentType.Contains("application/pdf", StringComparison.OrdinalIgnoreCase))
            return await WriteErrorResponseAsync(req, 400, "Příloha musí být ve formátu PDF.");

        var bytes = await ReadBodyBytesWithLimitAsync(req.Body, MaxAttachmentBytes);
        if (bytes.Length == 0) return await WriteErrorResponseAsync(req, 400, "Prázdný soubor.");

        var blobPath = $"{id}/faktura.pdf";
        await _blobStorageService.UploadAsync(BlobContainerNames.Finance, blobPath, bytes, "application/pdf");
        existing.AttachmentBlobName = blobPath;
        await _financialRecordRepository.UpsertAsync(existing);
        _logger.LogInformation("Attachment uploaded for finance record {RecordId} ({Size} bytes).", id, bytes.Length);
        return await WriteJsonResponseAsync(req, HttpStatusCode.OK, EntityMapper.ToResponse(existing));
    }
    catch (AppException ex) { return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message); }
    catch (Exception) { return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba."); }
}

[Function("DownloadFinanceAttachment")]
[RequireRole(UserRole.Admin, UserRole.Accountant)]
public async Task<HttpResponseData> DownloadFinanceAttachmentAsync(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "finance/{id}/attachment")] HttpRequestData req,
    string id)
{
    try
    {
        var existing = await FindFinancialRecordByIdAsync(id);
        if (existing is null) throw new NotFoundException("FinancialRecord", id);
        if (string.IsNullOrEmpty(existing.AttachmentBlobName))
            return await WriteErrorResponseAsync(req, 404, "Záznam nemá přílohu.");

        var stream = await _blobStorageService.DownloadAsync(BlobContainerNames.Finance, existing.AttachmentBlobName);
        if (stream is null) return await WriteErrorResponseAsync(req, 404, "Soubor nebyl ve storage nalezen.");

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/pdf");
        response.Headers.Add("Content-Disposition", $"attachment; filename=\"faktura-{id}.pdf\"");
        response.Body = new MemoryStream(ms.ToArray());
        return response;
    }
    catch (AppException ex) { return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message); }
    catch (Exception) { return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba."); }
}

private static async Task<byte[]> ReadBodyBytesWithLimitAsync(Stream body, long limit)
{
    using var ms = new MemoryStream();
    await body.CopyToAsync(ms);
    if (ms.Length > limit) throw new AppException("Soubor je příliš velký (max 20 MB).", 400);
    return ms.ToArray();
}
```
> `FindFinancialRecordByIdAsync` už v `FinanceFunctions` existuje (PK=year lookup). `BlobContainerNames.Finance` = "finance" už existuje.
> Route `finance/{id}/attachment` nekoliduje s `finance/fund`, `finance/summary`, `finance/balance`, `finance/export/*` (literály mají přednost) ani s `finance/{id}` (jiná délka segmentů).

**Zároveň při editaci opravit 2 anglické hlášky** ve stejném souboru: `"Invalid record type."` (ř. ~237 a ~301) → `"Neplatný typ záznamu."`.

**Kroky:** implementovat → build → celý test (žádná regrese) → commit `feat(api): PDF attachment upload/download for financial records`.

---

## Task 4: Oprava filtru roku u faktur za vodu (nález č. 2)

**Files:**
- Modify: `api/src/Oaza.Infrastructure/Persistence/SupplierInvoiceRepository.cs` — `GetByYearAsync` filtrovat podle `IssuedDate.Year` místo `Year`.
- Test: `api/tests/Oaza.Infrastructure.Tests/...` — pokud existuje SupplierInvoiceRepository test (jinak pokrytí v Task 1/frontend).

Změna: `all.Where(i => i.IssuedDate.Year == year)` místo `i.Year == year`. (Frontend dropdown se opraví v Task 7.)

> Ověřit, že `GetByYearAsync` nikde jinde nemá závislost na sémantice `Year` (settlement používá `GetByPartitionKeyAsync` + per-line-item, ne `GetByYearAsync` — potvrzeno v research).

**Kroky:** upravit → build → test → commit `fix(api): filter water invoices by issue-date year so multi-year invoices stay findable`.

---

## Task 5: Frontend typy + API klient

**Files:**
- Modify: `web/src/types/index.ts` — přidat `ReceivedInvoice` interface (zrcadlí DTO).
- Create: `web/src/api/receivedInvoices.ts` — `getReceivedInvoices(year?, category?)`.
- Modify: `web/src/api/finance.ts` — přidat `uploadFinanceAttachment` a `downloadFinanceAttachment` (po vzoru `invoices.ts`).

```ts
// types/index.ts
export interface ReceivedInvoice {
  source: 'voda' | 'ostatni';
  id: string;
  date: string;
  category: string;
  description: string;
  amount: number;
  dueDate: string | null;
  countsTowardWaterSettlement: boolean;
  hasAttachment: boolean;
  attachmentDownloadPath: string | null;
}
```
```ts
// api/receivedInvoices.ts
import { apiClient } from './client.ts';
import type { ReceivedInvoice } from '../types/index.ts';
export const getReceivedInvoices = (year?: number, category?: string): Promise<ReceivedInvoice[]> => {
  const p = new URLSearchParams();
  if (year !== undefined) p.set('year', String(year));
  if (category) p.set('category', category);
  const qs = p.toString();
  return apiClient.get<ReceivedInvoice[]>(`/invoices/all${qs ? `?${qs}` : ''}`);
};
```
`finance.ts` — `uploadFinanceAttachment(id, file, getToken)` a `downloadFinanceAttachment(id, filename, getToken)` doslova podle `invoices.ts` (jen cesta `/finance/{id}/attachment`).

**Kroky:** implementovat → `npm run build` + `npm run lint` → commit `feat(web): received-invoices api client + finance attachment helpers`.

---

## Task 6: Stránka „Přehled faktur" + routing + navigace

**Files:**
- Create: `web/src/pages/InvoicesOverviewPage.tsx`
- Modify: `web/src/App.tsx` — route `/prehled-faktur` s `<ProtectedRoute requiredRole="Accountant">` (povolí Admin+Účetní).
- Modify: `web/src/components/Layout.tsx` — nová child položka pod Hospodaření, viditelná Admin+Účetní.

**Stránka** (vzor: FinancePage/InvoicesSection styl):
- `useApi(() => getReceivedInvoices())` — načte vše.
- Filtry (in-memory): Rok (odvozeno z `new Set(data.map(d => new Date(d.date).getFullYear()))`, sestupně) + Kategorie (`Vše`/`Voda`/`Elektro`/`Údržba`/`Pojištění`/`Jiné`).
- Souhrn: součet částek filtru, zvlášť „voda" a „ostatní".
- Tabulka: Datum · Kategorie (badge) · Popis · Splatnost · Zdroj (badge „vstupuje do vyúčtování" pro voda) · Částka · Příloha (odkaz na stažení přes `downloadReceivedAttachment`, viz níže).
- Stažení přílohy: fetch `attachmentDownloadPath` s tokenem (helper v `receivedInvoices.ts` nebo přímo přes `finance`/`invoices` download podle `source`). Nejjednodušší: helper `downloadReceivedAttachment(item, getToken)` v `receivedInvoices.ts`, který fetchne `item.attachmentDownloadPath` a stáhne blob.

**Layout.tsx** — přidat do `children` pod Hospodaření:
```tsx
{ label: 'Přehled faktur', path: '/prehled-faktur', icon: <ReceiptText size={iconSize} />, financeManager: true },
```
Rozšířit `NavItem` o `financeManager?: boolean`; child filtr: `.filter((child) => (!child.adminOnly || isAdmin) && (!child.financeManager || isAdmin || isAccountant))`, kde `isAccountant = user?.role === 'Accountant'`. Import ikony `ReceiptText` z `lucide-react`.

**Kroky:** implementovat → `npm run build` + `npm run lint` → commit `feat(web): received-invoices overview page under Hospodaření`.

---

## Task 7: Oprava roku v seznamu „Faktury za vodu" (frontend)

**Files:**
- Modify: `web/src/components/InvoicesSection.tsx` — dropdown roků odvodit z reálných dat (min…max `IssuedDate` napříč fakturami), ne pevné okno `currentYear±window`.

Načíst všechny faktury jednou (bez `year`), odvodit roky z `new Date(i.issuedDate).getFullYear()`, tím se faktura stane dohledatelnou pod rokem vystavení. Filtr položek podle vybraného roku porovnávat na `issuedDate` rok (konzistentní s backend Task 4). Detaily podle aktuální struktury komponenty.

**Kroky:** implementovat → build + lint → commit `fix(web): derive invoice year filter from data (issue date) so no invoice is hidden`.

---

## Task 8: Přílohy ve FinancePage (upload + stažení)

**Files:**
- Modify: `web/src/pages/FinancePage.tsx` — formulář „Nový záznam"/editace dostane volitelný `<input type="file" accept="application/pdf">`; po vytvoření/uložení záznamu, pokud je zvolen soubor, zavolat `uploadFinanceAttachment(id, file, getToken)`. V tabulce záznamů přidat sloupec/ikonu Příloha s odkazem na `downloadFinanceAttachment` (zobrazit jen když `record.hasAttachment`).

> `FinanceResponse` už má `hasAttachment` (ověřeno v EntityMapper).

**Kroky:** implementovat → build + lint → commit `feat(web): attach PDF to financial records on the Hospodaření page`.

---

## Task 9: Dokumentace (CLAUDE.md)

**Files:**
- Modify: `CLAUDE.md` — do „Implementation additions" přidat odstavec o `GET /invoices/all` + `GetReceivedInvoicesUseCase`, přílohách FinancialRecordu (`POST/GET /finance/{id}/attachment`, kontejner `finance`), nové stránce `/prehled-faktur`, a sjednocené definici roku faktur podle `IssuedDate`.

**Kroky:** upravit → commit `docs: document received-invoices overview + finance attachments in CLAUDE.md`.

---

## Finální verifikace
- `cd api && dotnet build Oaza.sln -c Release` → 0 chyb.
- `dotnet test Oaza.sln -c Release` → vše zelené (integr. testy skip bez Azurite).
- `cd web && npm run build && npm run lint` → čisté.
- Push `develop` → DEV deploy → manuální proklik: nová stránka, filtry, upload/stažení přílohy, dohledatelnost víceleté faktury.
