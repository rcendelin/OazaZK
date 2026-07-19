# Vodní fond a cena vody při uzávěrce — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an admin, while closing a water billing period, optionally draw an amount from the existing shared common fund (split evenly across active houses) and optionally carry the period's realized invoice price/m³ forward as the new recommended water-advance price.

**Architecture:** Reuses the existing two-step preview→close flow entirely unchanged for the base settlement math. The fund draw is implemented as ordinary `Doplatek` `AdvancePayment` rows (one per active house, flagged `IsFundTransfer=true`) plus one `FinancialRecord` expense, created inside `CloseBillingPeriodUseCase` *before* the final settlement recalculation — so `CalculateSettlementUseCase`'s existing advance-netting picks them up with zero changes to that class. The price update reuses the existing `AdvanceSettings` entity/repository. A new `GetFundBalanceUseCase` is extracted from `FinanceFunctions` (which today computes the fund balance inline) so the same calculation is shared by the read endpoint and the new close-time validation, instead of being duplicated a second time.

**Tech Stack:** .NET 8 Azure Functions (Isolated Worker), Azure.Data.Tables, FluentValidation, xUnit + Moq + FluentAssertions (backend); React 19 + TypeScript, Vite, TailwindCSS (frontend, no test runner — manual verification only).

**Spec:** `docs/superpowers/specs/2026-07-18-vodni-fond-a-cena-design.md`

---

### Task 1: Extract `GetFundBalanceUseCase`

Today `FinanceFunctions.GetFundBalanceAsync` (`api/src/Oaza.Functions/Endpoints/FinanceFunctions.cs:51-84`) computes the common-fund balance inline. Task 6 needs the exact same calculation inside `CloseBillingPeriodUseCase`. Extracting it now avoids duplicating the formula a second time (the kind of duplication the 2026-07-18 repo audit already flagged elsewhere in this codebase).

**Files:**
- Create: `api/src/Oaza.Application/UseCases/GetFundBalanceUseCase.cs`
- Test: `api/tests/Oaza.Application.Tests/UseCases/GetFundBalanceUseCaseTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Moq;
using Oaza.Application.UseCases;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.UseCases;

public class GetFundBalanceUseCaseTests
{
    private readonly Mock<IHouseRepository> _houseRepo = new();
    private readonly Mock<IAdvancePaymentRepository> _advanceRepo = new();
    private readonly Mock<IFinancialRecordRepository> _financialRecordRepo = new();
    private readonly GetFundBalanceUseCase _sut;

    public GetFundBalanceUseCaseTests()
    {
        _sut = new GetFundBalanceUseCase(_houseRepo.Object, _advanceRepo.Object, _financialRecordRepo.Object);
    }

    [Fact]
    public async Task CalculateAsync_SumsCommonContributions_MinusNonWaterElectroExpenses()
    {
        _houseRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.House)).ReturnsAsync(new List<House>
        {
            new() { Id = "house-1", Name = "A", IsActive = true },
            new() { Id = "house-2", Name = "B", IsActive = false },
        });
        _advanceRepo.Setup(r => r.GetByHouseIdAsync("house-1")).ReturnsAsync(new List<AdvancePayment>
        {
            new() { HouseId = "house-1", Type = PaymentType.Advance, CommonAmount = 500m },
        });
        _advanceRepo.Setup(r => r.GetByHouseIdAsync("house-2")).ReturnsAsync(new List<AdvancePayment>
        {
            new() { HouseId = "house-2", Type = PaymentType.Doplatek, CommonAmount = 300m },
            new() { HouseId = "house-2", Type = PaymentType.Payout, CommonAmount = 1000m }, // excluded: Payout is net-level
        });
        _financialRecordRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<FinancialRecord>
        {
            new() { Type = FinancialRecordType.Expense, Category = "udrzba", Amount = 200m },
            new() { Type = FinancialRecordType.Expense, Category = "voda", Amount = 9999m }, // excluded: water settled separately
            new() { Type = FinancialRecordType.Income, Category = "jine", Amount = 9999m }, // excluded: not an expense
        });

        var result = await _sut.CalculateAsync();

        result.CommonContributions.Should().Be(800m);
        result.ExtraordinaryCosts.Should().Be(200m);
        result.FundBalance.Should().Be(600m);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `api/`): `dotnet test Oaza.sln --filter GetFundBalanceUseCaseTests`
Expected: FAIL to build — `GetFundBalanceUseCase` does not exist.

- [ ] **Step 3: Write the use case**

```csharp
using Oaza.Domain.Constants;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.UseCases;

/// <summary>
/// Computes the common fund balance: money collected toward the common base
/// across all households, minus extraordinary expenses (anything that isn't
/// water or electricity, which are settled through their own contributions).
/// Shared by the read endpoint (GET /finance/fund) and the water-settlement
/// close flow, which can draw part of this balance into the water vyúčtování.
/// </summary>
public class GetFundBalanceUseCase
{
    private readonly IHouseRepository _houseRepository;
    private readonly IAdvancePaymentRepository _advanceRepository;
    private readonly IFinancialRecordRepository _financialRecordRepository;

    public GetFundBalanceUseCase(
        IHouseRepository houseRepository,
        IAdvancePaymentRepository advanceRepository,
        IFinancialRecordRepository financialRecordRepository)
    {
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _advanceRepository = advanceRepository ?? throw new ArgumentNullException(nameof(advanceRepository));
        _financialRecordRepository = financialRecordRepository ?? throw new ArgumentNullException(nameof(financialRecordRepository));
    }

    public async Task<FundBalanceResult> CalculateAsync()
    {
        var houses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
        decimal commonContributions = 0m;
        foreach (var house in houses)
        {
            var payments = await _advanceRepository.GetByHouseIdAsync(house.Id);
            commonContributions += payments
                .Where(p => p.Type is PaymentType.Advance or PaymentType.Doplatek)
                .Sum(p => p.CommonAmount);
        }

        var records = await _financialRecordRepository.GetAllAsync();
        var extraordinaryCosts = records
            .Where(r => r.Type == FinancialRecordType.Expense
                && !string.Equals(r.Category, "voda", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(r.Category, "elektro", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.Amount);

        return new FundBalanceResult(
            Math.Round(commonContributions, 2),
            Math.Round(extraordinaryCosts, 2),
            Math.Round(commonContributions - extraordinaryCosts, 2));
    }
}

public record FundBalanceResult(decimal CommonContributions, decimal ExtraordinaryCosts, decimal FundBalance);
```

Note: matches `FinanceFunctions.GetFundBalanceAsync`'s current behavior exactly — it does **not** filter houses by `IsActive` (neither does the code being extracted), so this preserves today's behavior unchanged.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Oaza.sln --filter GetFundBalanceUseCaseTests`
Expected: PASS

- [ ] **Step 5: Register in DI**

Modify `api/src/Oaza.Functions/Program.cs` — add this block near the other use-case registrations (e.g. right before the `CalculateSettlementUseCase` registration at line 69):

```csharp
        // Use cases: Common fund balance (shared by GET /finance/fund and water-settlement close)
        services.AddSingleton<GetFundBalanceUseCase>(sp =>
            new GetFundBalanceUseCase(
                sp.GetRequiredService<IHouseRepository>(),
                sp.GetRequiredService<IAdvancePaymentRepository>(),
                sp.GetRequiredService<IFinancialRecordRepository>()));
```

- [ ] **Step 6: Build**

Run (from `api/`): `dotnet build Oaza.sln --configuration Release`
Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add api/src/Oaza.Application/UseCases/GetFundBalanceUseCase.cs api/tests/Oaza.Application.Tests/UseCases/GetFundBalanceUseCaseTests.cs api/src/Oaza.Functions/Program.cs
git commit -m "feat: extract GetFundBalanceUseCase from FinanceFunctions"
```

---

### Task 2: Wire `GetFundBalanceUseCase` into `FinanceFunctions.GetFundBalanceAsync`

**Files:**
- Modify: `api/src/Oaza.Functions/Endpoints/FinanceFunctions.cs:20-84`

- [ ] **Step 1: Add the new constructor dependency**

In `FinanceFunctions.cs`, add a field and constructor parameter (existing fields/params at lines 22-48):

```csharp
    private readonly IFinancialRecordRepository _financialRecordRepository;
    private readonly IAdvancePaymentRepository _advanceRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly GetFundBalanceUseCase _getFundBalanceUseCase;
    private readonly GenerateFinanceReportUseCase _generatePdfUseCase;
    private readonly GenerateFinanceExcelUseCase _generateExcelUseCase;
    private readonly ILogger<FinanceFunctions> _logger;
```

```csharp
    public FinanceFunctions(
        IFinancialRecordRepository financialRecordRepository,
        IAdvancePaymentRepository advanceRepository,
        IHouseRepository houseRepository,
        GetFundBalanceUseCase getFundBalanceUseCase,
        GenerateFinanceReportUseCase generatePdfUseCase,
        GenerateFinanceExcelUseCase generateExcelUseCase,
        ILogger<FinanceFunctions> logger)
    {
        _financialRecordRepository = financialRecordRepository ?? throw new ArgumentNullException(nameof(financialRecordRepository));
        _advanceRepository = advanceRepository ?? throw new ArgumentNullException(nameof(advanceRepository));
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _getFundBalanceUseCase = getFundBalanceUseCase ?? throw new ArgumentNullException(nameof(getFundBalanceUseCase));
        _generatePdfUseCase = generatePdfUseCase ?? throw new ArgumentNullException(nameof(generatePdfUseCase));
        _generateExcelUseCase = generateExcelUseCase ?? throw new ArgumentNullException(nameof(generateExcelUseCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
```

- [ ] **Step 2: Replace the inline calculation**

Replace the body of `GetFundBalanceAsync` (lines 55-84) with:

```csharp
    [Function("GetFundBalance")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public async Task<HttpResponseData> GetFundBalanceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "finance/fund")] HttpRequestData req)
    {
        try
        {
            var result = await _getFundBalanceUseCase.CalculateAsync();
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, new
            {
                commonContributions = result.CommonContributions,
                extraordinaryCosts = result.ExtraordinaryCosts,
                fundBalance = result.FundBalance,
            });
        }
        catch (AppException ex) { return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating fund balance.");
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }
```

(Since Azure Functions Isolated Worker resolves endpoint classes straight from DI by constructor, no separate registration for `FinanceFunctions` itself is needed — only the new `GetFundBalanceUseCase` dependency from Task 1 needs to be resolvable, which it now is.)

- [ ] **Step 3: Build**

Run: `dotnet build Oaza.sln --configuration Release`
Expected: 0 errors.

- [ ] **Step 4: Run the full test suite to confirm no regression**

Run: `dotnet test Oaza.sln --configuration Release`
Expected: same pass count as before this task (116 passed / 3 skipped), 0 failed.

- [ ] **Step 5: Commit**

```bash
git add api/src/Oaza.Functions/Endpoints/FinanceFunctions.cs
git commit -m "refactor: FinanceFunctions.GetFundBalanceAsync delegates to GetFundBalanceUseCase"
```

---

### Task 3: Add `IsFundTransfer` to `AdvancePayment`

**Files:**
- Modify: `api/src/Oaza.Domain/Entities/AdvancePayment.cs`
- Modify: `api/src/Oaza.Infrastructure/Persistence/TableEntityMapper.cs:211-249`
- Modify: `api/src/Oaza.Application/DTOs/AdvanceDtos.cs:35-49`
- Modify: `api/src/Oaza.Application/Mapping/EntityMapper.cs:65-82`
- Modify: `web/src/types/index.ts:84-97`

- [ ] **Step 1: Add the domain field**

In `AdvancePayment.cs`, add after the `Note` property (after line 34):

```csharp
    /// <summary>Optional free-text note (mainly for doplatky).</summary>
    public string? Note { get; set; }

    /// <summary>
    /// True when this doplatek was auto-generated by closing a water billing
    /// period with a fund draw (see CloseBillingPeriodUseCase), not manually
    /// entered by an admin. Drives the "Z fondu" badge in the payment history.
    /// </summary>
    public bool IsFundTransfer { get; set; }
```

- [ ] **Step 2: Persist and read the field in the Table Storage mapper**

In `TableEntityMapper.cs`, `ToTableEntity(AdvancePayment payment)` (lines 211-231), add the new attribute before the closing brace:

```csharp
        return new TableEntity(payment.HouseId, rowKey)
        {
            { "Year", payment.Year },
            { "Month", payment.Month },
            { "Amount", payment.Amount.ToString("G29", CultureInfo.InvariantCulture) },
            { "WaterAmount", payment.WaterAmount.ToString("G29", CultureInfo.InvariantCulture) },
            { "ElectricityAmount", payment.ElectricityAmount.ToString("G29", CultureInfo.InvariantCulture) },
            { "CommonAmount", payment.CommonAmount.ToString("G29", CultureInfo.InvariantCulture) },
            { "PaymentDate", DateTime.SpecifyKind(payment.PaymentDate, DateTimeKind.Utc) },
            { "Type", payment.Type.ToString() },
            { "Note", payment.Note },
            { "IsFundTransfer", payment.IsFundTransfer }
        };
```

In `ToAdvancePayment(TableEntity entity)` (lines 233-249), add to the object initializer:

```csharp
        return new AdvancePayment
        {
            HouseId = entity.PartitionKey,
            RowKey = entity.RowKey,
            Year = entity.GetInt32("Year") ?? 0,
            Month = entity.GetInt32("Month") ?? 0,
            Amount = decimal.TryParse(entity.GetString("Amount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) ? amount : 0m,
            WaterAmount = decimal.TryParse(entity.GetString("WaterAmount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var water) ? water : 0m,
            ElectricityAmount = decimal.TryParse(entity.GetString("ElectricityAmount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var elec) ? elec : 0m,
            CommonAmount = decimal.TryParse(entity.GetString("CommonAmount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var common) ? common : 0m,
            PaymentDate = entity.GetDateTimeOffset("PaymentDate")?.UtcDateTime ?? DateTime.MinValue,
            Type = Enum.TryParse<PaymentType>(entity.GetString("Type"), out var type) ? type : PaymentType.Advance,
            Note = entity.GetString("Note"),
            IsFundTransfer = entity.GetBoolean("IsFundTransfer") ?? false
        };
```

- [ ] **Step 3: Expose it on the API response DTO**

In `api/src/Oaza.Application/DTOs/AdvanceDtos.cs`, add to `AdvanceResponse` (after line 47, `Note`):

```csharp
public class AdvanceResponse
{
    public string HouseId { get; set; } = string.Empty;
    public string? HouseName { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public decimal WaterAmount { get; set; }
    public decimal ElectricityAmount { get; set; }
    public decimal CommonAmount { get; set; }
    public DateTime PaymentDate { get; set; }
    public string Type { get; set; } = "Advance";
    public string? Note { get; set; }
    public bool IsFundTransfer { get; set; }
    public string RowKey { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Map it through**

In `api/src/Oaza.Application/Mapping/EntityMapper.cs`, `ToResponse(AdvancePayment payment, ...)` (lines 65-82):

```csharp
    public static AdvanceResponse ToResponse(AdvancePayment payment, string? houseName = null)
    {
        return new AdvanceResponse
        {
            HouseId = payment.HouseId,
            HouseName = houseName,
            Year = payment.Year,
            Month = payment.Month,
            Amount = payment.Amount,
            WaterAmount = payment.WaterAmount,
            ElectricityAmount = payment.ElectricityAmount,
            CommonAmount = payment.CommonAmount,
            PaymentDate = payment.PaymentDate,
            Type = payment.Type.ToString(),
            Note = payment.Note,
            IsFundTransfer = payment.IsFundTransfer,
            RowKey = payment.RowKey,
        };
    }
```

- [ ] **Step 5: Mirror the field on the frontend type**

In `web/src/types/index.ts`, `AdvancePayment` interface (lines 84-97):

```typescript
export interface AdvancePayment {
  houseId: string;
  houseName: string | null;
  year: number;
  month: number;
  amount: number;
  waterAmount: number;
  electricityAmount: number;
  commonAmount: number;
  paymentDate: string;
  type: PaymentType;
  note: string | null;
  isFundTransfer: boolean;
  rowKey: string;
}
```

- [ ] **Step 6: Build backend and frontend**

Run: `dotnet build Oaza.sln --configuration Release` (from `api/`) — expected 0 errors.
Run: `npm run build` (from `web/`) — expected success (this is a pure type addition, no logic uses it yet, so `tsc` will not complain about an unused field on an interface).

- [ ] **Step 7: Commit**

```bash
git add api/src/Oaza.Domain/Entities/AdvancePayment.cs api/src/Oaza.Infrastructure/Persistence/TableEntityMapper.cs api/src/Oaza.Application/DTOs/AdvanceDtos.cs api/src/Oaza.Application/Mapping/EntityMapper.cs web/src/types/index.ts
git commit -m "feat: add IsFundTransfer flag to AdvancePayment"
```

---

### Task 4: Add `"fond-voda"` finance category

**Files:**
- Modify: `api/src/Oaza.Application/Validators/CreateFinanceRequestValidator.cs:9`
- Modify: `api/src/Oaza.Application/Validators/UpdateFinanceRequestValidator.cs:9` (mirror of Create; same line)

- [ ] **Step 1: Write the failing test**

Check `api/tests/Oaza.Application.Tests/Validators/CreateFinanceRequestValidatorTests.cs` for the existing test class shape, then add:

```csharp
    [Fact]
    public async Task Validate_CategoryFondVoda_IsAllowed()
    {
        var request = new CreateFinanceRequest
        {
            Type = "Expense",
            Category = "fond-voda",
            Amount = 100m,
            Date = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            Description = "test",
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }
```

Add this `[Fact]` inside the existing `CreateFinanceRequestValidatorTests` class (`api/tests/Oaza.Application.Tests/Validators/CreateFinanceRequestValidatorTests.cs`), which already has a `private readonly CreateFinanceRequestValidator _sut = new();` field — reuse it, don't declare a new one.

(Match the existing test class's field name for the validator instance — e.g. `_validator` — exactly as already used by the other tests in that file.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Oaza.sln --filter Validate_CategoryFondVoda_IsAllowed`
Expected: FAIL — `"fond-voda"` is not in `AllowedCategories`, validation error "Category must be one of: voda, elektro, udrzba, pojisteni, jine."

- [ ] **Step 3: Add the category**

In `CreateFinanceRequestValidator.cs` line 9:

```csharp
    private static readonly string[] AllowedCategories = { "voda", "elektro", "udrzba", "pojisteni", "jine", "fond-voda" };
```

Make the identical change in `UpdateFinanceRequestValidator.cs` (same line, same array).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Oaza.sln --filter Validate_CategoryFondVoda_IsAllowed`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add api/src/Oaza.Application/Validators/CreateFinanceRequestValidator.cs api/src/Oaza.Application/Validators/UpdateFinanceRequestValidator.cs api/tests/Oaza.Application.Tests/Validators/CreateFinanceRequestValidatorTests.cs
git commit -m "feat: allow fond-voda as a financial record category"
```

---

### Task 5: Extend the close-period request DTO

**Files:**
- Modify: `api/src/Oaza.Application/DTOs/SettlementDtos.cs:50-52`

- [ ] **Step 1: Add the new fields**

```csharp
public record CalculateSettlementRequest(
    string LossAllocationMethod, // "Equal" or "ProportionalToConsumption"
    decimal FundDrawAmount = 0m,
    bool ApplyNewWaterPrice = false,
    DateTime? NewWaterPriceValidFrom = null
);
```

- [ ] **Step 2: Build**

Run: `dotnet build Oaza.sln --configuration Release`
Expected: 0 errors (all optional, so the existing single-argument JSON body used by `POST /billing-periods/{id}/close` today still deserializes fine).

- [ ] **Step 3: Commit**

```bash
git add api/src/Oaza.Application/DTOs/SettlementDtos.cs
git commit -m "feat: extend CalculateSettlementRequest with fund-draw and price-update fields"
```

---

### Task 6: Extend `CloseBillingPeriodUseCase` with fund draw + price update

This is the core of the feature. Everything else in this plan exists to support this task.

**Files:**
- Modify: `api/src/Oaza.Application/UseCases/CloseBillingPeriodUseCase.cs`
- Modify: `api/src/Oaza.Functions/Program.cs` (DI registration)
- Modify: `api/tests/Oaza.Application.Tests/UseCases/CloseBillingPeriodUseCaseTests.cs`

- [ ] **Step 1: Update the existing test's fixture to support the new dependencies**

Replace the top of `CloseBillingPeriodUseCaseTests.cs` (lines 1-42) with:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Oaza.Application.Exceptions;
using Oaza.Application.UseCases;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.UseCases;

public class CloseBillingPeriodUseCaseTests
{
    private readonly Mock<IBillingPeriodRepository> _billingRepo = new();
    private readonly Mock<IHouseRepository> _houseRepo = new();
    private readonly Mock<IWaterMeterRepository> _meterRepo = new();
    private readonly Mock<IMeterReadingRepository> _readingRepo = new();
    private readonly Mock<ISupplierInvoiceRepository> _invoiceRepo = new();
    private readonly Mock<IAdvancePaymentRepository> _advanceRepo = new();
    private readonly Mock<IAdvanceSettingsRepository> _settingsRepo = new();
    private readonly Mock<ISettlementRepository> _settlementRepo = new();
    private readonly Mock<IFinancialRecordRepository> _financialRecordRepo = new();

    private readonly CloseBillingPeriodUseCase _sut;
    private readonly Dictionary<string, List<AdvancePayment>> _paymentsByHouse = new();

    private static readonly DateTime Start = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc);

    public CloseBillingPeriodUseCaseTests()
    {
        _settingsRepo.Setup(r => r.GetAsync()).ReturnsAsync(new AdvanceSettings());
        _financialRecordRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<FinancialRecord>());

        var calc = new CalculateSettlementUseCase(
            _billingRepo.Object, _houseRepo.Object, _meterRepo.Object,
            _readingRepo.Object, _invoiceRepo.Object, _advanceRepo.Object,
            _settingsRepo.Object,
            Mock.Of<ILogger<CalculateSettlementUseCase>>());

        var fundBalanceUseCase = new GetFundBalanceUseCase(
            _houseRepo.Object, _advanceRepo.Object, _financialRecordRepo.Object);

        _sut = new CloseBillingPeriodUseCase(
            calc, _billingRepo.Object, _settlementRepo.Object,
            _houseRepo.Object, _advanceRepo.Object, _financialRecordRepo.Object, _settingsRepo.Object,
            fundBalanceUseCase,
            Mock.Of<ILogger<CloseBillingPeriodUseCase>>());
    }

    private BillingPeriod SetupScenario(BillingPeriodStatus status = BillingPeriodStatus.Open)
    {
        var period = new BillingPeriod
        {
            Id = "period-1",
            Name = "H1 2025",
            DateFrom = Start,
            DateTo = End,
            Status = status,
        };
        _billingRepo.Setup(r => r.GetAsync(PartitionKeys.Period, "period-1")).ReturnsAsync(period);

        _houseRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.House)).ReturnsAsync(new List<House>
        {
            new() { Id = "house-1", Name = "A", IsActive = true },
            new() { Id = "house-2", Name = "B", IsActive = true },
        });
        _meterRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Meter)).ReturnsAsync(new List<WaterMeter>
        {
            new() { Id = "main", Type = MeterType.Main },
            new() { Id = "m1", Type = MeterType.Individual, HouseId = "house-1" },
            new() { Id = "m2", Type = MeterType.Individual, HouseId = "house-2" },
        });
        Readings("main", (Start, 100m), (End, 200m)); // 100
        Readings("m1", (Start, 50m), (End, 80m));      // 30
        Readings("m2", (Start, 20m), (End, 80m));      // 60  -> loss 10, equal 5 each
        _invoiceRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Invoice)).ReturnsAsync(new List<SupplierInvoice>
        {
            new() { Id = "inv", Year = 2025, Month = 3, Amount = 10000m },
        });
        Advances("house-1", 3000m);
        Advances("house-2", 5000m);
        return period;
    }

    private void Readings(string meterId, params (DateTime date, decimal value)[] readings) =>
        _readingRepo.Setup(r => r.GetByMeterIdAsync(meterId)).ReturnsAsync(
            readings.Select(x => new MeterReading
            {
                MeterId = meterId,
                ReadingDate = x.date,
                Value = x.value,
                Source = ReadingSource.Manual,
                ImportedAt = DateTime.UtcNow,
                ImportedBy = "test",
            }).ToList());

    private void Advances(string houseId, decimal waterAmount, decimal commonAmount = 0m)
    {
        var list = new List<AdvancePayment>
        {
            new() { HouseId = houseId, Year = 2025, Month = 3, Amount = waterAmount + commonAmount, WaterAmount = waterAmount, CommonAmount = commonAmount },
        };
        _paymentsByHouse[houseId] = list;
        _advanceRepo.Setup(r => r.GetByHouseAndPeriodAsync(houseId, Start, End)).ReturnsAsync(() => _paymentsByHouse[houseId]);
        _advanceRepo.Setup(r => r.GetByHouseIdAsync(houseId)).ReturnsAsync(() => _paymentsByHouse[houseId]);
        _advanceRepo.Setup(r => r.UpsertAsync(It.Is<AdvancePayment>(p => p.HouseId == houseId)))
            .Callback<AdvancePayment>(p => _paymentsByHouse[houseId].Add(p))
            .Returns(Task.CompletedTask);
    }
```

Keep the two existing `[Fact]` tests (`CloseAsync_PersistsOneSettlementPerHouse_AndLocksPeriod`, `CloseAsync_AlreadyClosedPeriod_Throws_AndPersistsNothing`) exactly as they are below this — they call `_sut.CloseAsync("period-1", LossAllocationMethod.Equal)` with no fund/price arguments, which still compiles once those parameters are optional (Step 3 below), and still pass unchanged since a `fundDrawAmount` of `0m` and `applyNewWaterPrice` of `false` must be a no-op.

- [ ] **Step 2: Run the existing tests to verify they still fail to build**

Run: `dotnet test Oaza.sln --filter CloseBillingPeriodUseCaseTests`
Expected: FAIL to build — `CloseBillingPeriodUseCase` constructor doesn't accept the new arguments yet.

- [ ] **Step 3: Extend `CloseBillingPeriodUseCase`**

Replace the whole file:

```csharp
using Microsoft.Extensions.Logging;
using Oaza.Application.Exceptions;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.UseCases;

/// <summary>
/// Closes a billing period: recomputes the final settlement, persists one
/// Settlement entity per house, and flips the period to Closed. This is the
/// irreversible money-committing operation (business rule #5), so it re-verifies
/// the period is still Open after calculation before writing anything.
///
/// Optionally, before the final calculation: draws an admin-chosen amount from
/// the shared common fund, split evenly across active houses as a doplatek
/// per house (see docs/superpowers/specs/2026-07-18-vodni-fond-a-cena-design.md),
/// and/or carries the period's realized invoice price/m³ forward as the new
/// recommended water-advance price.
/// </summary>
public class CloseBillingPeriodUseCase
{
    private readonly CalculateSettlementUseCase _calculateSettlementUseCase;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly IAdvancePaymentRepository _advanceRepository;
    private readonly IFinancialRecordRepository _financialRecordRepository;
    private readonly IAdvanceSettingsRepository _advanceSettingsRepository;
    private readonly GetFundBalanceUseCase _getFundBalanceUseCase;
    private readonly ILogger<CloseBillingPeriodUseCase> _logger;

    public CloseBillingPeriodUseCase(
        CalculateSettlementUseCase calculateSettlementUseCase,
        IBillingPeriodRepository billingPeriodRepository,
        ISettlementRepository settlementRepository,
        IHouseRepository houseRepository,
        IAdvancePaymentRepository advanceRepository,
        IFinancialRecordRepository financialRecordRepository,
        IAdvanceSettingsRepository advanceSettingsRepository,
        GetFundBalanceUseCase getFundBalanceUseCase,
        ILogger<CloseBillingPeriodUseCase> logger)
    {
        _calculateSettlementUseCase = calculateSettlementUseCase ?? throw new ArgumentNullException(nameof(calculateSettlementUseCase));
        _billingPeriodRepository = billingPeriodRepository ?? throw new ArgumentNullException(nameof(billingPeriodRepository));
        _settlementRepository = settlementRepository ?? throw new ArgumentNullException(nameof(settlementRepository));
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _advanceRepository = advanceRepository ?? throw new ArgumentNullException(nameof(advanceRepository));
        _financialRecordRepository = financialRecordRepository ?? throw new ArgumentNullException(nameof(financialRecordRepository));
        _advanceSettingsRepository = advanceSettingsRepository ?? throw new ArgumentNullException(nameof(advanceSettingsRepository));
        _getFundBalanceUseCase = getFundBalanceUseCase ?? throw new ArgumentNullException(nameof(getFundBalanceUseCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Calculates and persists settlements for the period, then locks it.
    /// Returns the persisted Settlement entities.
    /// </summary>
    public async Task<IReadOnlyList<Settlement>> CloseAsync(
        string periodId,
        LossAllocationMethod lossAllocationMethod,
        decimal fundDrawAmount = 0m,
        bool applyNewWaterPrice = false,
        DateTime? newWaterPriceValidFrom = null)
    {
        // Calculate final settlement numbers (also validates the period is Open).
        var preview = await _calculateSettlementUseCase.CalculateAsync(periodId, lossAllocationMethod);

        // Re-verify the period is still Open before committing the irreversible close.
        var period = await _billingPeriodRepository.GetAsync(PartitionKeys.Period, periodId)
            ?? throw new NotFoundException("BillingPeriod", periodId);

        if (period.Status != BillingPeriodStatus.Open)
        {
            throw new AppException("Billing period is already closed. Cannot close again.");
        }

        if (fundDrawAmount < 0)
        {
            throw new AppException("Fund draw amount cannot be negative.");
        }

        if (fundDrawAmount > 0)
        {
            await ApplyFundDrawAsync(period, fundDrawAmount);

            // The fund doplatky now exist in the ledger — recompute so Balance
            // reflects them (CalculateSettlementUseCase itself is unchanged).
            preview = await _calculateSettlementUseCase.CalculateAsync(periodId, lossAllocationMethod);
        }

        if (applyNewWaterPrice)
        {
            await ApplyNewWaterPriceAsync(preview, newWaterPriceValidFrom);
        }

        // Save one settlement per house.
        var settlements = new List<Settlement>();
        foreach (var houseDetail in preview.Houses)
        {
            var settlement = new Settlement
            {
                PeriodId = periodId,
                HouseId = houseDetail.HouseId,
                ConsumptionM3 = houseDetail.ConsumptionM3,
                SharePercent = houseDetail.SharePercent,
                CalculatedAmount = houseDetail.CalculatedAmount,
                TotalAdvances = houseDetail.TotalAdvances,
                Balance = houseDetail.Balance,
                LossAllocatedM3 = houseDetail.LossAllocatedM3,
                ElectricityCharge = houseDetail.ElectricityCharge,
                ElectricityAdvances = houseDetail.ElectricityAdvances,
                CommonCharge = houseDetail.CommonCharge,
                CommonAdvances = houseDetail.CommonAdvances,
            };

            await _settlementRepository.UpsertAsync(settlement);
            settlements.Add(settlement);
        }

        // Lock the period (irreversible).
        period.Status = BillingPeriodStatus.Closed;
        await _billingPeriodRepository.UpsertAsync(period);

        _logger.LogInformation(
            "Billing period {PeriodId} ({Name}) closed with {Count} settlements.",
            periodId, period.Name, settlements.Count);

        return settlements;
    }

    /// <summary>
    /// Draws <paramref name="fundDrawAmount"/> from the shared common fund and
    /// splits it evenly across active houses as one doplatek each, plus one
    /// FinancialRecord expense that reduces the fund's future balance.
    /// </summary>
    private async Task ApplyFundDrawAsync(BillingPeriod period, decimal fundDrawAmount)
    {
        var houses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
        var activeHouses = houses.Where(h => h.IsActive).ToList();

        if (activeHouses.Count == 0)
        {
            throw new AppException("Cannot draw from the fund: no active houses.");
        }

        var fundBalance = await _getFundBalanceUseCase.CalculateAsync();
        if (fundDrawAmount > fundBalance.FundBalance)
        {
            throw new AppException(
                $"Fund draw amount ({fundDrawAmount:F2} Kč) exceeds the available fund balance ({fundBalance.FundBalance:F2} Kč).");
        }

        var perHouse = Math.Round(fundDrawAmount / activeHouses.Count, 2);
        var paymentDate = DateTime.SpecifyKind(period.DateTo, DateTimeKind.Utc);

        // RowKey/Id are DETERMINISTIC (derived only from period.Id), not a random
        // guid, on purpose: if CloseAsync is retried after a partial failure (e.g.
        // a crash between this batch and the final Settlement writes), re-running
        // it with the same or a corrected fundDrawAmount must overwrite these same
        // rows rather than create duplicates. This relies on Azure.Data.Tables'
        // UpsertAsync being a true upsert (same PK+RK replaces), the same guarantee
        // CloseBillingPeriodUseCase already relies on for the per-house Settlement
        // writes below.
        foreach (var house in activeHouses)
        {
            var doplatek = new AdvancePayment
            {
                HouseId = house.Id,
                Year = paymentDate.Year,
                Month = paymentDate.Month,
                Amount = perHouse,
                WaterAmount = perHouse,
                ElectricityAmount = 0m,
                CommonAmount = 0m,
                PaymentDate = paymentDate,
                Type = PaymentType.Doplatek,
                IsFundTransfer = true,
                Note = $"Použito ze společného fondu — {period.Name}",
                RowKey = $"FUND-{period.Id}",
            };
            await _advanceRepository.UpsertAsync(doplatek);
        }

        var expense = new FinancialRecord
        {
            Id = $"fund-{period.Id}",
            Year = paymentDate.Year,
            Type = FinancialRecordType.Expense,
            Category = "fond-voda",
            Amount = fundDrawAmount,
            Date = paymentDate,
            Description = $"Čerpání společného fondu pro vyúčtování vody — {period.Name}",
        };
        await _financialRecordRepository.UpsertAsync(expense);
    }

    /// <summary>
    /// Carries the period's realized invoice price/m³ (incl. loss) forward as
    /// the new recommended water-advance price.
    /// </summary>
    private async Task ApplyNewWaterPriceAsync(DTOs.SettlementPreviewResponse preview, DateTime? newWaterPriceValidFrom)
    {
        if (newWaterPriceValidFrom is null)
        {
            throw new AppException("New water price valid-from date is required when applying a new price.");
        }

        var totalWaterM3 = preview.TotalHouseConsumption + preview.TotalLoss;
        if (totalWaterM3 <= 0)
        {
            throw new AppException("Cannot derive a new water price: total consumption for the period is zero.");
        }

        var effectivePrice = Math.Round(preview.TotalInvoiceAmount / totalWaterM3, 2);

        var settings = await _advanceSettingsRepository.GetAsync();
        settings.WaterPricePerM3 = effectivePrice;
        settings.WaterPriceValidFrom = DateTime.SpecifyKind(newWaterPriceValidFrom.Value, DateTimeKind.Utc);
        await _advanceSettingsRepository.UpsertAsync(settings);
    }
}
```

- [ ] **Step 4: Update Program.cs registration**

In `api/src/Oaza.Functions/Program.cs`, replace the `CloseBillingPeriodUseCase` registration (originally lines 81-86):

```csharp
        // Use cases: Billing period close (persist settlements + lock period)
        services.AddSingleton<CloseBillingPeriodUseCase>(sp =>
            new CloseBillingPeriodUseCase(
                sp.GetRequiredService<CalculateSettlementUseCase>(),
                sp.GetRequiredService<IBillingPeriodRepository>(),
                sp.GetRequiredService<ISettlementRepository>(),
                sp.GetRequiredService<IHouseRepository>(),
                sp.GetRequiredService<IAdvancePaymentRepository>(),
                sp.GetRequiredService<IFinancialRecordRepository>(),
                sp.GetRequiredService<IAdvanceSettingsRepository>(),
                sp.GetRequiredService<GetFundBalanceUseCase>(),
                sp.GetRequiredService<ILogger<CloseBillingPeriodUseCase>>()));
```

- [ ] **Step 5: Run the existing tests to verify they pass unchanged**

Run: `dotnet test Oaza.sln --filter CloseBillingPeriodUseCaseTests`
Expected: PASS — both original tests (`CloseAsync_PersistsOneSettlementPerHouse_AndLocksPeriod`, `CloseAsync_AlreadyClosedPeriod_Throws_AndPersistsNothing`) still pass with `fundDrawAmount` defaulting to `0m`.

- [ ] **Step 6: Add the fund-draw test**

Append to `CloseBillingPeriodUseCaseTests.cs`:

```csharp
    [Fact]
    public async Task CloseAsync_WithFundDraw_CreatesOneDoplatekPerActiveHouse_OneExpense_AndReducesBalance()
    {
        // Arrange: give both houses a common-fund contribution so the fund has 10,000 Kč available.
        var period = SetupScenario();
        Advances("house-1", 3000m, commonAmount: 5000m);
        Advances("house-2", 5000m, commonAmount: 5000m);
        _settlementRepo.Setup(r => r.UpsertAsync(It.IsAny<Settlement>())).Returns(Task.CompletedTask);
        _billingRepo.Setup(r => r.UpsertAsync(It.IsAny<BillingPeriod>())).Returns(Task.CompletedTask);

        // Act: draw 800 Kč, split evenly across the 2 active houses (400 Kč each).
        var result = await _sut.CloseAsync("period-1", LossAllocationMethod.Equal, fundDrawAmount: 800m);

        // Assert: one fund doplatek per active house.
        _advanceRepo.Verify(r => r.UpsertAsync(It.Is<AdvancePayment>(p =>
            p.HouseId == "house-1" && p.WaterAmount == 400m && p.Type == PaymentType.Doplatek && p.IsFundTransfer)), Times.Once);
        _advanceRepo.Verify(r => r.UpsertAsync(It.Is<AdvancePayment>(p =>
            p.HouseId == "house-2" && p.WaterAmount == 400m && p.Type == PaymentType.Doplatek && p.IsFundTransfer)), Times.Once);

        // Assert: one financial-record expense reducing the fund.
        _financialRecordRepo.Verify(r => r.UpsertAsync(It.Is<FinancialRecord>(f =>
            f.Category == "fond-voda" && f.Amount == 800m && f.Type == FinancialRecordType.Expense)), Times.Once);

        // Assert: the persisted Settlement.Balance already reflects the fund credit
        // (house-1's balance was 500 before the fund draw — see the base test — minus the 400 Kč contribution).
        var houseOne = result.Single(s => s.HouseId == "house-1");
        houseOne.Balance.Should().Be(100m);
        houseOne.TotalAdvances.Should().Be(3400m); // 3000 original + 400 fund doplatek
    }

    [Fact]
    public async Task CloseAsync_FundDrawExceedsBalance_Throws_AndPersistsNothing()
    {
        // Arrange: no common-fund contributions at all -> fund balance is 0.
        SetupScenario();

        // Act
        var act = () => _sut.CloseAsync("period-1", LossAllocationMethod.Equal, fundDrawAmount: 100m);

        // Assert
        await act.Should().ThrowAsync<AppException>().WithMessage("*exceeds*");
        _advanceRepo.Verify(r => r.UpsertAsync(It.Is<AdvancePayment>(p => p.IsFundTransfer)), Times.Never);
        _financialRecordRepo.Verify(r => r.UpsertAsync(It.IsAny<FinancialRecord>()), Times.Never);
        _settlementRepo.Verify(r => r.UpsertAsync(It.IsAny<Settlement>()), Times.Never);
        _billingRepo.Verify(r => r.UpsertAsync(It.IsAny<BillingPeriod>()), Times.Never);
    }

    [Fact]
    public async Task CloseAsync_ApplyNewWaterPrice_UpdatesSettings_UsingRealizedPricePerM3()
    {
        // Arrange: SetupScenario has TotalInvoiceAmount=10000, TotalHouseConsumption=90, TotalLoss=10 -> 100 Kč/m3.
        SetupScenario();
        _settlementRepo.Setup(r => r.UpsertAsync(It.IsAny<Settlement>())).Returns(Task.CompletedTask);
        _billingRepo.Setup(r => r.UpsertAsync(It.IsAny<BillingPeriod>())).Returns(Task.CompletedTask);
        var validFrom = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        await _sut.CloseAsync("period-1", LossAllocationMethod.Equal,
            applyNewWaterPrice: true, newWaterPriceValidFrom: validFrom);

        // Assert
        _settingsRepo.Verify(r => r.UpsertAsync(It.Is<AdvanceSettings>(s =>
            s.WaterPricePerM3 == 100m && s.WaterPriceValidFrom == validFrom)), Times.Once);
    }

    [Fact]
    public async Task CloseAsync_ApplyNewWaterPrice_WithoutValidFromDate_Throws()
    {
        SetupScenario();

        var act = () => _sut.CloseAsync("period-1", LossAllocationMethod.Equal, applyNewWaterPrice: true);

        await act.Should().ThrowAsync<AppException>();
        _settingsRepo.Verify(r => r.UpsertAsync(It.IsAny<AdvanceSettings>()), Times.Never);
    }
```

- [ ] **Step 7: Run all four new tests plus the two original ones**

Run: `dotnet test Oaza.sln --filter CloseBillingPeriodUseCaseTests`
Expected: PASS — 6 tests total, 0 failed.

- [ ] **Step 8: Run the full backend test suite**

Run (from `api/`): `dotnet test Oaza.sln --configuration Release`
Expected: previous 119 total + 5 new (`GetFundBalanceUseCaseTests` ×1, category test ×1, `CloseBillingPeriodUseCaseTests` ×4 new — 2 already existed) = 125 total, 0 failed, 3 skipped (Azurite).

- [ ] **Step 9: Commit**

```bash
git add api/src/Oaza.Application/UseCases/CloseBillingPeriodUseCase.cs api/src/Oaza.Functions/Program.cs api/tests/Oaza.Application.Tests/UseCases/CloseBillingPeriodUseCaseTests.cs
git commit -m "feat: support fund draw and water-price carry-forward when closing a billing period"
```

---

### Task 7: Wire the new fields through the close endpoint

**Files:**
- Modify: `api/src/Oaza.Functions/Endpoints/BillingPeriodFunctions.cs:249-291`

- [ ] **Step 1: Pass the new fields from the request body to `CloseAsync`**

Replace the body-parsing and `CloseAsync` call inside `CloseBillingPeriodAsync` (originally lines 260-271):

```csharp
            // Parse loss allocation method + optional fund draw / price update from the request body.
            var request = await JsonSerializer.DeserializeAsync<CalculateSettlementRequest>(req.Body, JsonOptions);
            var lossMethod = LossAllocationMethod.Equal;
            if (request is not null &&
                !string.IsNullOrEmpty(request.LossAllocationMethod) &&
                Enum.TryParse<LossAllocationMethod>(request.LossAllocationMethod, ignoreCase: true, out var parsed))
            {
                lossMethod = parsed;
            }

            var fundDrawAmount = request?.FundDrawAmount ?? 0m;
            var applyNewWaterPrice = request?.ApplyNewWaterPrice ?? false;
            var newWaterPriceValidFrom = request?.NewWaterPriceValidFrom;

            // Calculate, persist settlements and lock the period (irreversible).
            var settlements = await _closeBillingPeriodUseCase.CloseAsync(
                id, lossMethod, fundDrawAmount, applyNewWaterPrice, newWaterPriceValidFrom);
```

- [ ] **Step 2: Build**

Run: `dotnet build Oaza.sln --configuration Release`
Expected: 0 errors.

- [ ] **Step 3: Run the full backend test suite**

Run: `dotnet test Oaza.sln --configuration Release`
Expected: same as Task 6 Step 8 — no regression.

- [ ] **Step 4: Commit**

```bash
git add api/src/Oaza.Functions/Endpoints/BillingPeriodFunctions.cs
git commit -m "feat: accept fund-draw and water-price fields on POST /billing-periods/{id}/close"
```

---

### Task 8: Frontend — API client for the new close fields

**Files:**
- Modify: `web/src/api/billing.ts:34-40`

- [ ] **Step 1: Extend `closeBillingPeriod`**

```typescript
export const closeBillingPeriod = (
  periodId: string,
  method: string,
  options?: {
    fundDrawAmount?: number;
    applyNewWaterPrice?: boolean;
    newWaterPriceValidFrom?: string;
  },
): Promise<void> =>
  apiClient.post<void>(`/billing-periods/${encodeURIComponent(periodId)}/close`, {
    lossAllocationMethod: method,
    fundDrawAmount: options?.fundDrawAmount ?? 0,
    applyNewWaterPrice: options?.applyNewWaterPrice ?? false,
    newWaterPriceValidFrom: options?.newWaterPriceValidFrom ?? null,
  });
```

(`options` is optional so every other existing caller of `closeBillingPeriod(periodId, method)` keeps compiling unchanged.)

- [ ] **Step 2: Build**

Run (from `web/`): `npm run build`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add web/src/api/billing.ts
git commit -m "feat: closeBillingPeriod accepts fund-draw and water-price options"
```

---

### Task 9: Frontend — fund-draw slider + price section on the settlement screen

**Files:**
- Modify: `web/src/pages/BillingPage.tsx` (the `OpenPeriodDetail` component, starting line 368)

- [ ] **Step 1: Add imports and state**

Near the top of `BillingPage.tsx`, add to the existing import from `../api/billing.ts` / add a new import:

```typescript
import { getFundBalance } from '../api/finance';
import { getHouses } from '../api/houses';
import { getAdvanceSettings } from '../api/advanceSettings';
```

Inside `OpenPeriodDetail`, after the existing state declarations (after line 391, `editError`):

```typescript
  const [fundBalance, setFundBalance] = useState<number | null>(null);
  const [activeHouseCount, setActiveHouseCount] = useState<number | null>(null);
  const [currentWaterPrice, setCurrentWaterPrice] = useState<{ price: number; validFrom: string } | null>(null);
  const [fundDraw, setFundDraw] = useState(0);
  const [applyNewPrice, setApplyNewPrice] = useState(true);
  const [newPriceValidFrom, setNewPriceValidFrom] = useState(period.dateTo.split('T')[0]);
```

- [ ] **Step 2: Load fund balance, active house count, and current water price once the preview is available**

Add after `handleCalculate` (after line 446):

```typescript
  const handleCalculate = async () => {
    if (inFlight.current) return;
    inFlight.current = true;
    setCalculating(true);
    setCalcError(null);
    try {
      const [result, fund, houses, settings] = await Promise.all([
        calculateSettlement(period.id, method),
        getFundBalance(),
        getHouses(),
        getAdvanceSettings(),
      ]);
      setPreview(result);
      setFundBalance(fund.fundBalance);
      setActiveHouseCount(houses.filter((h) => h.isActive).length);
      setCurrentWaterPrice({ price: settings.waterPricePerM3, validFrom: settings.waterPriceValidFrom });
      setFundDraw(0);
      setNewPriceValidFrom(
        new Date(new Date(period.dateTo).getTime() + 86_400_000).toISOString().split('T')[0],
      );
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'Nastala neočekávaná chyba';
      setCalcError(message);
    } finally {
      setCalculating(false);
      inFlight.current = false;
    }
  };
```

(This replaces the existing `handleCalculate` at lines 430-446 — the only change is fetching the three extra pieces of data alongside the settlement preview, and resetting the fund/price inputs to sensible defaults for the freshly loaded period.)

- [ ] **Step 3: Compute the derived live-preview numbers**

Add just before the `return` statement of `OpenPeriodDetail` (before line 468):

```typescript
  const effectiveWaterPrice =
    preview && preview.totalHouseConsumption + preview.totalLoss > 0
      ? preview.totalInvoiceAmount / (preview.totalHouseConsumption + preview.totalLoss)
      : null;
  const perHouseFundCredit =
    activeHouseCount && activeHouseCount > 0 ? fundDraw / activeHouseCount : 0;
```

- [ ] **Step 4: Add the two new sections and pass the fund credit into the settlement table**

Replace the `{/* Preview */}` block (lines 574-613) with:

```typescript
      {preview && (
        <div className="space-y-4">
          {/* Metric cards */}
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <MetricCard
              label="Faktura dodavatele"
              value={`${formatCZK(preview.totalInvoiceAmount)} Kč`}
            />
            <MetricCard
              label="Hlavní vodoměr"
              value={`${formatNumber(preview.mainMeterConsumption)} m³`}
            />
            <MetricCard
              label="Součet dílčích"
              value={`${formatNumber(preview.totalHouseConsumption)} m³`}
            />
            <MetricCard
              label="Ztráta"
              value={`${formatNumber(preview.totalLoss)} m³`}
              variant={preview.totalLoss > 0 ? 'warning' : 'normal'}
            />
          </div>

          {/* ① Fund draw */}
          {fundBalance !== null && fundBalance > 0 && activeHouseCount && (
            <div className="rounded-2xl border border-border bg-surface-raised p-4 shadow-card">
              <h3 className="text-sm font-semibold">① Čerpání ze společného fondu</h3>
              <p className="mt-1 text-sm text-text-secondary">
                Zůstatek fondu: <strong>{formatCZK(fundBalance)} Kč</strong>
              </p>
              <input
                type="range"
                min={0}
                max={fundBalance}
                step={100}
                value={fundDraw}
                onChange={(e) => setFundDraw(Number(e.target.value))}
                className="mt-2 w-full"
              />
              <div className="mt-2 flex justify-between text-sm">
                <span>Použít nyní: <strong>{formatCZK(fundDraw)} Kč</strong></span>
                <span>Na dům: <strong>{formatCZK(perHouseFundCredit)} Kč</strong></span>
                <span>Zbyde ve fondu: <strong>{formatCZK(fundBalance - fundDraw)} Kč</strong></span>
              </div>
            </div>
          )}

          {/* ② Water price carry-forward */}
          {effectiveWaterPrice !== null && currentWaterPrice && (
            <div className="rounded-2xl border border-border bg-surface-raised p-4 shadow-card">
              <h3 className="text-sm font-semibold">② Cena vody pro příští zálohy</h3>
              <p className="mt-1 text-sm text-text-secondary">
                Efektivní cena v tomto období: <strong>{formatCZK(effectiveWaterPrice)} Kč/m³</strong>
                {' '}(nyní nastaveno: {formatCZK(currentWaterPrice.price)} Kč/m³ od {formatDate(currentWaterPrice.validFrom)})
              </p>
              <label className="mt-2 flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={applyNewPrice}
                  onChange={(e) => setApplyNewPrice(e.target.checked)}
                />
                Nastavit {formatCZK(effectiveWaterPrice)} Kč/m³ jako novou cenu vody
              </label>
              {applyNewPrice && (
                <div className="mt-2 flex items-center gap-2 text-sm">
                  <span>platnou od</span>
                  <input
                    type="date"
                    value={newPriceValidFrom}
                    onChange={(e) => setNewPriceValidFrom(e.target.value)}
                    className="rounded-xl border border-border bg-surface-raised px-3 py-1.5 text-sm focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20"
                  />
                </div>
              )}
            </div>
          )}

          {/* Settlement table */}
          <SettlementTable houses={preview.houses} fundCreditPerHouse={perHouseFundCredit} />

          {/* Close button */}
          <div className="flex justify-end">
            <button
              onClick={() => setShowCloseConfirm(true)}
              disabled={closing}
              className="rounded-xl bg-danger px-4 py-2 text-sm font-medium text-white hover:bg-red-600 focus:outline-none focus:ring-2 focus:ring-red-500 focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {closing
                ? 'Uzavírání...'
                : 'Uzavřít období + generovat PDF'}
            </button>
          </div>
        </div>
      )}
```

- [ ] **Step 5: Pass the fund draw and price choice into `handleClose`**

Replace `handleClose` (lines 448-466):

```typescript
  const handleClose = async () => {
    if (inFlight.current) return;
    inFlight.current = true;
    setClosing(true);
    setCloseError(null);
    try {
      await closeBillingPeriod(period.id, method, {
        fundDrawAmount: fundDraw,
        applyNewWaterPrice: applyNewPrice && effectiveWaterPrice !== null,
        newWaterPriceValidFrom: newPriceValidFrom,
      });
      setShowCloseConfirm(false);
      onPeriodClosed();
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'Nastala neočekávaná chyba';
      setCloseError(message);
      setShowCloseConfirm(false);
    } finally {
      setClosing(false);
      inFlight.current = false;
    }
  };
```

- [ ] **Step 6: Extend `SettlementTable` to show the fund credit and final balance**

Replace the whole `SettlementTable` function (lines 730-843) with:

```typescript
function SettlementTable({
  houses,
  fundCreditPerHouse = 0,
}: {
  houses: SettlementPreviewResponse['houses'];
  fundCreditPerHouse?: number;
}) {
  const totals = houses.reduce(
    (acc, h) => ({
      consumption: acc.consumption + h.consumptionM3,
      loss: acc.loss + h.lossAllocatedM3,
      share: acc.share + h.sharePercent,
      amount: acc.amount + h.calculatedAmount,
      advances: acc.advances + h.totalAdvances,
      balance: acc.balance + h.balance,
      finalBalance: acc.finalBalance + (h.balance - fundCreditPerHouse),
    }),
    { consumption: 0, loss: 0, share: 0, amount: 0, advances: 0, balance: 0, finalBalance: 0 },
  );

  return (
    <div className="overflow-x-auto rounded-2xl border border-border">
      <table className="min-w-full divide-y divide-border">
        <thead className="bg-surface-sunken">
          <tr>
            <th className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wider text-text-muted">
              Dům
            </th>
            <th className="px-4 py-3 text-right text-xs font-semibold uppercase tracking-wider text-text-muted">
              Spotřeba m³
            </th>
            <th className="px-4 py-3 text-right text-xs font-semibold uppercase tracking-wider text-text-muted">
              Ztráta m³
            </th>
            <th className="px-4 py-3 text-right text-xs font-semibold uppercase tracking-wider text-text-muted">
              Podíl %
            </th>
            <th className="px-4 py-3 text-right text-xs font-semibold uppercase tracking-wider text-text-muted">
              Částka Kč
            </th>
            <th className="px-4 py-3 text-right text-xs font-semibold uppercase tracking-wider text-text-muted">
              Zálohy Kč
            </th>
            <th className="px-4 py-3 text-right text-xs font-semibold uppercase tracking-wider text-text-muted">
              Výsledek Kč
            </th>
            <th className="px-4 py-3 text-right text-xs font-semibold uppercase tracking-wider text-text-muted">
              Z fondu Kč
            </th>
            <th className="px-4 py-3 text-right text-xs font-semibold uppercase tracking-wider text-text-muted">
              Finální saldo Kč
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border bg-surface-raised">
          {houses.map((house) => {
            const finalBalance = house.balance - fundCreditPerHouse;
            return (
              <tr key={house.houseId} className="hover:bg-surface-sunken/50">
                <td className="whitespace-nowrap px-4 py-3 text-sm font-medium text-text-primary">
                  {house.houseName}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-text-secondary">
                  {formatNumber(house.consumptionM3)}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-text-secondary">
                  {formatNumber(house.lossAllocatedM3)}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-text-secondary">
                  {formatPercent(house.sharePercent)}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-text-secondary">
                  {formatCZK(house.calculatedAmount)}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-text-secondary">
                  {formatCZK(house.totalAdvances)}
                </td>
                <td
                  className={`whitespace-nowrap px-4 py-3 text-right text-sm font-mono font-semibold ${
                    house.balance > 0
                      ? 'text-danger'
                      : house.balance < 0
                        ? 'text-success'
                        : 'text-text-secondary'
                  }`}
                >
                  {formatCZK(house.balance)}
                  {house.balance > 0 && (
                    <span className="ml-1 text-xs font-normal">doplatek</span>
                  )}
                  {house.balance < 0 && (
                    <span className="ml-1 text-xs font-normal">přeplatek</span>
                  )}
                </td>
                <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-text-secondary">
                  {fundCreditPerHouse > 0 ? `-${formatCZK(fundCreditPerHouse)}` : '—'}
                </td>
                <td
                  className={`whitespace-nowrap px-4 py-3 text-right text-sm font-mono font-semibold ${
                    finalBalance > 0
                      ? 'text-danger'
                      : finalBalance < 0
                        ? 'text-success'
                        : 'text-text-secondary'
                  }`}
                >
                  {formatCZK(finalBalance)}
                </td>
              </tr>
            );
          })}
          {/* Totals row */}
          <tr className="bg-surface-sunken font-bold">
            <td className="whitespace-nowrap px-4 py-3 text-sm text-text-primary">
              Celkem
            </td>
            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-text-primary">
              {formatNumber(totals.consumption)}
            </td>
            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-text-primary">
              {formatNumber(totals.loss)}
            </td>
            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-text-primary">
              {formatPercent(totals.share)}
            </td>
            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-text-primary">
              {formatCZK(totals.amount)}
            </td>
            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-text-primary">
              {formatCZK(totals.advances)}
            </td>
            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-text-primary">
              {formatCZK(totals.balance)}
            </td>
            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-text-primary">
              {totals.balance - totals.finalBalance > 0 ? `-${formatCZK(totals.balance - totals.finalBalance)}` : '—'}
            </td>
            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-text-primary">
              {formatCZK(totals.finalBalance)}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  );
}
```

And add matching `<th>` headers ("Z fondu", "Finální saldo") next to the existing "Saldo" header in the same table's `<thead>`.

- [ ] **Step 7: Manual verification**

Run: `cd web && npm run dev`, then in the browser:
- Open an Open billing period with at least one supplier invoice recorded, click "Vypočítat vyúčtování".
- Confirm the fund section only appears when the fund balance is > 0, the slider moves from 0 to the fund balance, and the three numbers (used/per-house/remaining) update live.
- Confirm the price section shows the effective price vs. the currently configured one, and the date field defaults to the day after the period's end.
- Move the slider to a nonzero value, confirm the settlement table's "Finální saldo" column updates to `Saldo − Z fondu` for every row.
- Click "Uzavřít období...", confirm, then re-open the now-Closed period and check `Saldo` page for the new fund-transfer doplatek on each house.

- [ ] **Step 8: Commit**

```bash
git add web/src/pages/BillingPage.tsx
git commit -m "feat: fund-draw slider and water-price carry-forward on the settlement close screen"
```

---

### Task 10: Frontend — "Z fondu" badge in payment history

**Files:**
- Modify: `web/src/pages/SaldoPage.tsx:551-567` (the `PaymentsList` row rendering)

- [ ] **Step 1: Add the badge next to the note**

Replace the note cell (line 562):

```typescript
                <td className="px-2 py-2.5 text-text-muted max-w-[12rem] truncate">
                  {p.isFundTransfer && (
                    <span className="mr-1 rounded bg-accent/10 px-1.5 py-0.5 text-xs font-medium text-accent">
                      Z fondu
                    </span>
                  )}
                  {p.note}
                </td>
```

- [ ] **Step 2: Build**

Run: `npm run build`
Expected: success.

- [ ] **Step 3: Manual verification**

After closing a period with a nonzero fund draw (Task 9), open `/saldo`, find the affected houses' payment history, and confirm the "Z fondu" badge appears on the auto-generated doplatek rows and nowhere else.

- [ ] **Step 4: Commit**

```bash
git add web/src/pages/SaldoPage.tsx
git commit -m "feat: badge fund-transfer doplatky in the house payment history"
```

---

### Task 11: Final verification

- [ ] **Step 1: Full backend build + test**

Run (from `api/`):
```bash
dotnet build Oaza.sln --configuration Release
dotnet test Oaza.sln --configuration Release
```
Expected: 0 build errors/warnings; 125 tests passed, 3 skipped (Azurite not running locally), 0 failed.

- [ ] **Step 2: Full frontend build + lint**

Run (from `web/`):
```bash
npm run lint
npm run build
```
Expected: 0 lint errors; build succeeds (bundle-size warning is pre-existing and out of scope for this feature).

- [ ] **Step 3: Manual end-to-end click-through**

Repeat the Task 9 Step 7 and Task 10 Step 3 checks in one pass on a real Open period with real invoice + advances data, including the two edge cases from the spec:
- Set the slider to the maximum (equal to the full fund balance) and confirm it cannot be dragged past it.
- Uncheck the price checkbox and confirm `AdvanceSettings.WaterPricePerM3` is unchanged after closing.

- [ ] **Step 4: Update `CLAUDE.md`**

Add a short note to the "Implementation additions" section documenting: the `fond-voda` finance category, `AdvancePayment.IsFundTransfer`, and the close-time fund-draw + water-price-carry-forward behavior — following the same style as the existing "Component-split payments + per-house saldo" entry.

- [ ] **Step 5: Final commit**

```bash
git add CLAUDE.md
git commit -m "docs: document fund draw and water-price carry-forward in CLAUDE.md"
```
