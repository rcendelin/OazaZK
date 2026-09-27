using System.Globalization;
using FluentAssertions;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Ledger;
using Oaza.Application.OffBookFunds;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Services;

namespace Oaza.Functions.Tests.Golden;

/// <summary>
/// T14: the ZADANI §4 scenarios S1–S8 computed by the real use cases, saved as the JSON the API sends into
/// <c>web/e2e/golden/</c>. The Playwright specs (<c>web/e2e/golden.spec.ts</c>) serve these files as the mocked API
/// and check the numbers as the user sees them, so the UI tests run on real calculations. The key golden numbers
/// are asserted here too, so a fixture can never silently hold wrong numbers.
/// Regenerate after an intended change: <c>UPDATE_GOLDEN=1 dotnet test Oaza.sln --filter FullyQualifiedName~Golden</c>.
/// All scenarios live in this one class so xunit runs them one after another.
/// </summary>
public class GoldenFixturesTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private static DateTimeOffset At(int y, int m, int d) => new(y, m, d, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = D(2023, 11, 1);

    private static void House(GoldenWorld w, string id, bool ownershipPeriod = true)
    {
        _ = w.Houses.UpsertAsync(new House { Id = id, Name = $"RD {id}", ContactPerson = $"Vlastník {id}", Email = $"{id.ToLowerInvariant()}@example.cz", IsActive = true });
        if (ownershipPeriod)
            _ = w.Periods.UpsertAsync(new OwnershipPeriod { Id = OwnershipPeriod.KeyFor(id, Start), HouseId = id, OwnerName = $"Vlastník {id}", ValidFrom = Start });
    }

    private static void Component(GoldenWorld w, string id, string name, string code, AllocationMethod method, params string[] houses)
    {
        _ = w.Components.UpsertAsync(new CostComponent { Id = id, Name = name, Code = code, StartDate = Start, AllocationBasis = AllocationBasis.CostEntries });
        _ = w.Rules.UpsertAsync(new ComponentAllocationRule { Id = $"{id}-rule", ComponentId = id, ValidFrom = Start, Method = method });
        foreach (var house in houses)
            _ = w.Participations.UpsertAsync(new Participation { Id = $"{id}-{house}", ComponentId = id, HouseId = house, ValidFrom = Start });
    }

    private static void Reading(GoldenWorld w, string meter, DateOnly day, decimal value) =>
        _ = w.Readings.UpsertAsync(new MeterReading { MeterId = meter, ReadingDate = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Value = value, Source = ReadingSource.Manual });

    // ───────────────────── S1 – water losses (/voda) ─────────────────────

    [Theory]
    [InlineData(AllocationMethod.Equal, "S1-water-equal", new[] { 250.00, 250.00, 250.00, 250.00 })]
    [InlineData(AllocationMethod.Ratio, "S1-water-ratio", new[] { 444.44, 333.33, 166.67, 55.56 })]
    public async Task S1_WaterLosses(AllocationMethod method, string fixture, double[] expectedLoss)
    {
        var w = new GoldenWorld(At(2026, 7, 15));
        _ = w.Components.UpsertAsync(new CostComponent { Id = "pvk", Name = "Voda PVK", Code = "VODA_PVK", StartDate = Start, AllocationBasis = AllocationBasis.Metered, WaterRole = WaterRole.Consumption });
        _ = w.Components.UpsertAsync(new CostComponent { Id = "ztraty", Name = "Ztráty vody", Code = "ZTRATY_VODY", StartDate = Start, AllocationBasis = AllocationBasis.Metered, WaterRole = WaterRole.Losses });
        _ = w.Rules.UpsertAsync(new ComponentAllocationRule { Id = "ztraty-rule", ComponentId = "ztraty", ValidFrom = Start, Method = method, RatioSource = method == AllocationMethod.Ratio ? "VODA_PVK" : null });
        _ = w.Meters.UpsertAsync(new WaterMeter { Id = "main", MeterNumber = "H-001", Type = MeterType.Main });
        Reading(w, "main", D(2026, 1, 1), 500m);
        Reading(w, "main", D(2026, 7, 1), 600m); // 100 m³
        foreach (var (house, m3) in new[] { ("A", 40m), ("B", 30m), ("C", 15m), ("D", 5m) })
        {
            House(w, house);
            _ = w.Meters.UpsertAsync(new WaterMeter { Id = $"m{house}", MeterNumber = $"V-{house}", Type = MeterType.Individual, HouseId = house });
            Reading(w, $"m{house}", D(2026, 1, 1), 10m);
            Reading(w, $"m{house}", D(2026, 7, 1), 10m + m3);
            _ = w.Participations.UpsertAsync(new Participation { Id = $"pvk-{house}", ComponentId = "pvk", HouseId = house, ValidFrom = Start });
            _ = w.Participations.UpsertAsync(new Participation { Id = $"ztraty-{house}", ComponentId = "ztraty", HouseId = house, ValidFrom = Start });
        }
        // PVK invoice 10 000 Kč for 100 m³ → 100 Kč/m³, the 10 m³ loss costs 1 000,00 Kč.
        await w.CostEntries().CreateAsync("pvk", new SaveCostEntryRequest
        {
            Type = CostEntryType.OneOff, PeriodFrom = D(2026, 1, 1), PeriodTo = D(2026, 6, 30), Amount = 10_000m, QuantityM3 = 100m, Supplier = "PVK", ExternalRef = "S1-PVK",
        }, GoldenWorld.Actor);

        var result = await w.Water().CalculateAsync(D(2026, 1, 1), D(2026, 6, 30));

        var interval = result.Intervals.Should().ContainSingle().Subject;
        (interval.MainConsumptionM3, interval.HousesConsumptionM3, interval.LossM3, interval.LossCost).Should().Be((100m, 90m, 10m, 1_000m));
        interval.LossSegments.Should().ContainSingle().Which.Method.Should().Be(method);
        interval.Houses.Select(h => (h.HouseName, h.LossCost)).Should().Equal(
            new[] { "RD A", "RD B", "RD C", "RD D" }.Zip(expectedLoss.Select(x => (decimal)x)));
        interval.Houses.Sum(h => h.LossCost).Should().Be(1_000m);
        Golden.Verify(fixture, result);
    }

    // ───────────────────── S2 – waterworks credit (/saldo-domu) ─────────────────────

    [Fact]
    public async Task S2_WaterworksCredit()
    {
        var w = new GoldenWorld(At(2024, 1, 15));
        foreach (var house in new[] { "A", "B", "C", "D", "E", "F" })
            House(w, house);
        Component(w, "vodarna", "Elektřina – vodárna", "ELEKTRINA_VODARNA", AllocationMethod.Equal, "A", "B", "C", "D");
        await w.OpeningBalances().CreateAsync(new SaveOpeningBalanceRequest
        {
            Type = OpeningBalanceType.ComponentCredit, ComponentId = "vodarna", Date = Start, Value = -20_000m, Source = "vyúčtování dodavatele 2023",
        }, GoldenWorld.Actor);
        await w.CostEntries().CreateRecurringAsync("vodarna", new RecurringAdvanceRequest
        {
            Amount = 500m, From = D(2023, 11, 1), To = D(2023, 12, 31), Supplier = "Dodavatel elektřiny", PaidFrom = PaidFrom.SupplierCredit,
        }, GoldenWorld.Actor);

        var ledger = w.Ledger();
        var overview = await ledger.GetOverviewAsync(D(2023, 11, 1), D(2023, 12, 31));
        var a = await ledger.GetHouseLedgerAsync("A", null, D(2023, 11, 1), D(2023, 12, 31), GoldenWorld.Admin);
        var e = await ledger.GetHouseLedgerAsync("E", null, D(2023, 11, 1), D(2023, 12, 31), GoldenWorld.Admin);

        a.Items.Select(i => (i.Kind, i.Amount)).Should().Equal(
            (LedgerItemKind.Credit, 5_000m), (LedgerItemKind.Cost, -125m), (LedgerItemKind.Cost, -125m));
        a.Saldo.Should().Be(4_750m);
        e.Items.Should().BeEmpty();
        e.Saldo.Should().Be(0m);
        overview.Houses.Where(h => h.HouseId is "E" or "F").Should().OnlyContain(h => h.Saldo == 0m && h.Costs.GetValueOrDefault("vodarna") == 0m);
        Golden.Verify("S2-ledger", new { overview, houseA = a, houseE = e });
    }

    // ───────────────────── S3 – joining house (/saldo-domu) ─────────────────────

    [Fact]
    public async Task S3_JoiningHouse()
    {
        var w = new GoldenWorld(At(2027, 1, 15));
        foreach (var house in new[] { "A", "B", "C", "D", "E", "F" })
            House(w, house);
        Component(w, "osvetleni", "Osvětlení", "ELEKTRINA_OSVETLENI", AllocationMethod.Equal, "A", "B", "C", "D");
        await w.CostComponents().AddParticipationAsync("osvetleni", new AddParticipationRequest { HouseId = "E", ValidFrom = D(2026, 10, 1), Reason = "napojení domu E" }, GoldenWorld.Actor);
        await w.CostEntries().CreateAsync("osvetleni", new SaveCostEntryRequest
        {
            Type = CostEntryType.Settlement, PeriodFrom = D(2026, 7, 1), PeriodTo = D(2026, 12, 31), Amount = 1_840m, Supplier = "Dodavatel elektřiny", ExternalRef = "S3-VYUCTOVANI",
        }, GoldenWorld.Actor);

        var overview = await w.Ledger().GetOverviewAsync(D(2026, 7, 1), D(2026, 12, 31));

        overview.Houses.Select(h => (h.HouseName, h.Costs.GetValueOrDefault("osvetleni"))).Should().Equal(
            ("RD A", 414m), ("RD B", 414m), ("RD C", 414m), ("RD D", 414m), ("RD E", 184m), ("RD F", 0m));
        var control = overview.Components.Single(c => c.ComponentId == "osvetleni");
        (control.AllocatedTotal, control.HousesTotal, control.Matches).Should().Be((1_840m, 1_840m, true));
        Golden.Verify("S3-ledger", new { overview });
    }

    // ───────────────────── S4 – house transfer (/admin/opening-balances, /saldo-domu) ─────────────────────

    [Fact]
    public async Task S4_HouseTransfer()
    {
        var sale = D(2026, 3, 15);
        var w = new GoldenWorld(At(2026, 4, 10));
        foreach (var house in new[] { "A", "B", "C", "D" })
            House(w, house);
        Component(w, "vodarna", "Elektřina – vodárna", "ELEKTRINA_VODARNA", AllocationMethod.Equal, "A", "B", "C", "D");
        _ = w.Meters.UpsertAsync(new WaterMeter { Id = "mB", MeterNumber = "V-B", Type = MeterType.Individual, HouseId = "B" });
        Reading(w, "mB", D(2026, 3, 1), 1_230m);
        Reading(w, "mB", D(2026, 4, 1), 1_240m);
        await w.CostEntries().CreateAsync("vodarna", new SaveCostEntryRequest
        {
            Type = CostEntryType.Advance, PeriodFrom = D(2026, 3, 1), PeriodTo = D(2026, 3, 31), Amount = 3_100m, Supplier = "Dodavatel elektřiny", ExternalRef = "S4-ZALOHA-03",
        }, GoldenWorld.Actor);
        _ = w.Payments.UpsertAsync(new AdvancePayment { HouseId = "B", RowKey = "2026-03", Type = PaymentType.Advance, Year = 2026, Month = 3, Amount = 1_000m, WaterAmount = 1_000m, PaymentDate = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc) });
        var openings = w.OpeningBalances();
        var periodsBefore = await openings.GetOwnershipPeriodsAsync();
        var balancesBefore = await openings.ListAsync();

        var preview = await w.HouseTransfer().PreviewAsync("B", sale);
        var transfer = await w.HouseTransfer().TransferAsync("B", new HouseTransferRequest
        {
            TransferDate = sale, NewOwnerName = "Noví vlastníci B", NewOwnerContact = "novi.b@example.cz", MeterValue = 1_234.567m, MeterSource = "předávací protokol", FundShare = 0m,
        }, GoldenWorld.Actor);

        var periodsAfter = await openings.GetOwnershipPeriodsAsync();
        var balancesAfter = await openings.ListAsync();
        var ledger = w.Ledger();
        var overview = await ledger.GetOverviewAsync(null, null);
        var ledgerNew = await ledger.GetHouseLedgerAsync("B", null, null, null, GoldenWorld.Admin);
        var ledgerOld = await ledger.GetHouseLedgerAsync("B", OwnershipPeriod.KeyFor("B", Start), null, null, GoldenWorld.Admin);

        (preview.ClosingDate, preview.ClosingCosts, preview.ClosingPayments, preview.ClosingSaldo).Should().Be((D(2026, 3, 14), 350m, 1_000m, 650m));
        preview.SuggestedMeterValue.Should().Be(1_234.516m);
        transfer.ClosingSaldo.Should().Be(650m);
        balancesAfter.Should().ContainSingle(b => b.Type == OpeningBalanceType.MeterReading && b.MeterId == "mB" && b.Date == sale && b.Value == 1_234.567m);
        balancesAfter.Should().ContainSingle(b => b.Type == OpeningBalanceType.FundShare && b.HouseId == "B" && b.Date == sale && b.Value == 0m);
        (ledgerNew.OwnershipPeriod!.OwnerName, ledgerNew.From, ledgerNew.Opening, ledgerNew.Costs, ledgerNew.Payments).Should().Be(("Noví vlastníci B", sale, 0m, 425m, 0m));
        (ledgerOld.To, ledgerOld.Costs, ledgerOld.Payments, ledgerOld.Saldo).Should().Be((D(2026, 3, 14), 350m, 1_000m, 650m));
        Golden.Verify("S4-house-transfer", new { periodsBefore, balancesBefore, preview, transfer, periodsAfter, balancesAfter, overview, ledgerNew, ledgerOld });
    }

    // ───────────────────── S5 – cash book (/pokladna) ─────────────────────

    [Fact]
    public async Task S5_CashBook()
    {
        var w = new GoldenWorld(At(2026, 9, 27));
        var cash = w.CashBookUseCase();
        var depositRequest = new CreateCashBookEntryRequest
        {
            Date = D(2026, 9, 1), Type = CashBookEntryType.Deposit, Amount = 5_000m, Category = "výběr z účtu", Description = "Výběr z účtu", HasReceipt = true, BankTransactionRef = "výpis 9/2026",
        };
        var expenseRequest = new CreateCashBookEntryRequest
        {
            Date = D(2026, 9, 2), Type = CashBookEntryType.Expense, Amount = 2_000m, Category = "údržba okolí", Description = "úprava okolí", Counterparty = "Zahradník Vzorový", HasReceipt = false,
        };
        var rejectedRequest = new CreateCashBookEntryRequest
        {
            Date = D(2026, 9, 3), Type = CashBookEntryType.Expense, Amount = 4_000m, Category = "údržba okolí", Description = "další úprava okolí", Counterparty = "Zahradník Vzorový", HasReceipt = false,
        };
        var stornoRequest = new StornoCashBookEntryRequest { Reason = "zaplaceno z účtu" };

        var initial = await cash.ListAsync(null, null);
        var deposit = await cash.CreateAsync(depositRequest, GoldenWorld.Actor);
        var afterDeposit = await cash.ListAsync(null, null);
        var expense = await cash.CreateAsync(expenseRequest, GoldenWorld.Actor);
        var afterExpense = await cash.ListAsync(null, null);
        var tooMuch = () => cash.CreateAsync(rejectedRequest, GoldenWorld.Actor);
        var rejected = (await tooMuch.Should().ThrowAsync<BusinessRuleException>()).Which;
        var afterRejected = await cash.ListAsync(null, null);
        var storno = await cash.StornoAsync(expense.Id, stornoRequest, GoldenWorld.Actor);
        var afterStorno = await cash.ListAsync(null, null);

        initial.Entries.Should().BeEmpty();
        afterDeposit.ClosingBalance.Should().Be(5_000m);
        afterExpense.ClosingBalance.Should().Be(3_000m);
        rejected.Message.Should().Contain("záporný zůstatek");
        afterRejected.ClosingBalance.Should().Be(3_000m);
        afterRejected.Entries.Should().HaveCount(2);
        (storno.Type, storno.Effect, storno.Balance).Should().Be((CashBookEntryType.Correction, 2_000m, 5_000m));
        afterStorno.ClosingBalance.Should().Be(5_000m);
        Golden.Verify("S5-cash-book", new
        {
            requests = new { deposit = depositRequest, expense = expenseRequest, rejected = rejectedRequest, storno = stornoRequest },
            initial, deposit, afterDeposit, expense, afterExpense, rejected = Golden.BusinessRuleError(rejected), storno, afterStorno,
        });
    }

    // ───────────────────── S6 – off-book fund (/fond) ─────────────────────

    [Fact]
    public async Task S6_OffBookFund()
    {
        var houses = new[] { "A", "B", "C", "D", "E", "F" };
        var w = new GoldenWorld(At(2026, 12, 20));
        foreach (var house in houses)
            House(w, house);
        var fundUseCase = w.OffBookFund();
        var fund = await fundUseCase.CreateAsync(new SaveOffBookFundRequest { Name = "Fond na ohňostroje", ManagerName = "Karel Vzorový", AccountDescription = "soukromý účet správce" }, GoldenWorld.Actor);
        var call = await fundUseCase.AddRecordAsync(fund.Id, new AddFundRecordRequest
        {
            Kind = FundRecordKind.Call, Date = D(2026, 12, 1), DueDate = D(2026, 12, 15), Amount = 1_230m, Text = "Silvestr 2026", HouseIds = [.. houses],
        }, GoldenWorld.Actor);
        var day = 5;
        foreach (var house in houses.Take(5))
            await fundUseCase.AddRecordAsync(fund.Id, new AddFundRecordRequest { Kind = FundRecordKind.Contribution, Date = D(2026, 12, day++), Amount = 1_230m, HouseId = house, CallId = call.Id }, GoldenWorld.Actor);

        var funds = await fundUseCase.ListAsync();
        var detail = await fundUseCase.GetAsync(fund.Id);

        detail.Fund.Balance.Should().Be(6_150m);
        var status = detail.Calls.Should().ContainSingle().Subject;
        status.Debtors.Should().Be(1);
        status.Houses.Should().ContainSingle(h => !h.IsPaid).Which.HouseName.Should().Be("RD F");
        Golden.Verify("S6-off-book-fund", new { features = new { offBookFund = true }, funds, detail });
    }

    // ───────────────────── S7 – rounding (/naklady) ─────────────────────

    [Fact]
    public async Task S7_Rounding()
    {
        var w = new GoldenWorld(At(2026, 6, 1));
        foreach (var house in new[] { "A", "B", "C" })
            House(w, house);
        Component(w, "zelen", "Údržba zeleně", "UDRZBA_ZELENE", AllocationMethod.Equal, "A", "B", "C");
        var entry = await w.CostEntries().CreateAsync("zelen", new SaveCostEntryRequest
        {
            Type = CostEntryType.OneOff, PeriodFrom = D(2026, 5, 15), PeriodTo = D(2026, 5, 15), Amount = 1_000m, Supplier = "Zahradnictví Vzor", ExternalRef = "S7-SEKANI",
        }, GoldenWorld.Actor);

        var components = await w.CostComponents().ListAsync();
        var entries = await w.CostEntries().ListAsync("zelen", null, null);
        var allocation = await w.CostEntries().GetAllocationAsync("zelen", entry.Id);

        allocation.HouseTotals.Select(s => (s.HouseName, s.Amount)).Should().Equal(("RD A", 333.34m), ("RD B", 333.33m), ("RD C", 333.33m));
        allocation.HouseTotals.Sum(s => s.Amount).Should().Be(1_000m);
        Golden.Verify("S7-cost-allocation", new { components, entries, allocation });
    }

    // ───────────────────── S8 – reading interpolation (/admin/opening-balances) ─────────────────────

    [Fact]
    public void S8_ReadingInterpolation()
    {
        var w = new GoldenWorld(At(2026, 9, 27));
        _ = w.Meters.UpsertAsync(new WaterMeter { Id = "m1", MeterNumber = "V-001", Type = MeterType.Individual, HouseId = "A" });
        Reading(w, "m1", D(2023, 5, 22), 100m);
        Reading(w, "m1", D(2025, 1, 19), 700m);
        var date = DateTime.ParseExact("2023-11-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Same domain service and response shape as ReadingFunctions.EstimateReadingAsync (GET /readings/estimate).
        var estimate = ReadingEstimator.Estimate(w.Readings.Items.Values, date);
        var response = new ReadingEstimateResponse
        {
            MeterId = "m1",
            TargetDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
            Value = estimate.Value,
            IsEstimate = estimate.IsEstimate,
            Method = estimate.Method.ToString(),
            Note = estimate.Note,
        };

        (response.Value, response.IsEstimate, response.Method).Should().Be((260.855m, true, "Interpolated"));
        Golden.Verify("S8-reading-estimate", new { estimate = response });
    }
}
