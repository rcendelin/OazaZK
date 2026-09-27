using FluentAssertions;
using Oaza.Application.Exceptions;
using Oaza.Application.Ledger;
using Oaza.Application.Tests.TestSupport;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Time;

namespace Oaza.Application.Tests.UseCases;

public class HouseLedgerUseCaseTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private static readonly DateOnly Start = D(2023, 11, 1);
    private static readonly LedgerRequester Admin = new(UserRole.Admin, null);

    private readonly MemoryComponents _components = new();
    private readonly MemoryRules _rules = new();
    private readonly MemoryParticipations _participations = new();
    private readonly MemoryCostEntries _entries = new();
    private readonly MemoryOpeningBalances _openings = new();
    private readonly MemoryMeters _meters = new();
    private readonly MemoryReadings _readings = new();
    private readonly MemoryHouses _houses = new();
    private readonly MemoryPayments _payments = new();
    private readonly MemoryOwnershipPeriods _periods = new();

    public HouseLedgerUseCaseTests()
    {
        foreach (var name in new[] { "A", "B", "C", "D", "E", "F" })
        {
            _houses.UpsertAsync(new House { Id = name, Name = $"RD {name}", IsActive = true });
            _periods.UpsertAsync(new OwnershipPeriod { Id = OwnershipPeriod.KeyFor(name, Start), HouseId = name, OwnerName = $"Vlastník {name}", ValidFrom = Start });
        }
        // Elektřina – vodárna: A–D, EQUAL (R2).
        _components.UpsertAsync(new CostComponent { Id = "vodarna", Name = "Elektřina – vodárna", Code = "ELEKTRINA_VODARNA", StartDate = Start, AllocationBasis = AllocationBasis.CostEntries });
        _rules.UpsertAsync(new ComponentAllocationRule { Id = "r1", ComponentId = "vodarna", ValidFrom = Start, Method = AllocationMethod.Equal });
        foreach (var house in new[] { "A", "B", "C", "D" })
            _participations.UpsertAsync(new Participation { Id = $"v{house}", ComponentId = "vodarna", HouseId = house, ValidFrom = Start });
    }

    private HouseLedgerUseCase Sut() => new(
        new LedgerCostCollector(_components, _rules, _participations, _entries, _openings, _meters, _readings, _houses),
        _houses, _payments, _openings, _periods, _components,
        new PragueClock(new FixedTimeProvider(new DateTimeOffset(2023, 12, 31, 12, 0, 0, TimeSpan.Zero))));

    private void Advance(string componentId, DateOnly month, decimal amount, PaidFrom paidFrom) =>
        _entries.UpsertAsync(new CostEntry
        {
            Id = $"{componentId}-{month:yyyyMM}", ComponentId = componentId, Type = CostEntryType.Advance,
            PeriodFrom = month, PeriodTo = month.AddMonths(1).AddDays(-1), Amount = amount, PaidFrom = paidFrom,
        });

    [Fact]
    public async Task S2_WaterworksCreditAndAdvancesFromTheCredit()
    {
        await _openings.UpsertAsync(new OpeningBalance { Key = "ComponentCredit|vodarna|-", Type = OpeningBalanceType.ComponentCredit, ComponentId = "vodarna", Date = Start, Value = -20_000m, Source = "PRE" });
        Advance("vodarna", D(2023, 11, 1), 500m, PaidFrom.SupplierCredit);
        Advance("vodarna", D(2023, 12, 1), 500m, PaidFrom.SupplierCredit);

        var a = await Sut().GetHouseLedgerAsync("A", null, Start, D(2023, 12, 31), Admin);
        var e = await Sut().GetHouseLedgerAsync("E", null, Start, D(2023, 12, 31), Admin);

        a.Items.Select(i => (i.Kind, i.Amount)).Should().Equal(
            (LedgerItemKind.Credit, 5_000m), (LedgerItemKind.Cost, -125m), (LedgerItemKind.Cost, -125m));
        a.Saldo.Should().Be(4_750m); // the house still has a credit (přeplatek)
        a.Items.Select(i => i.Balance).Should().Equal(5_000m, 4_875m, 4_750m);
        a.Items[0].Detail!.Explanation.Should().Contain("rovným dílem mezi 4 domů");
        e.Items.Should().BeEmpty();
        e.Saldo.Should().Be(0m);
    }

    [Fact]
    public async Task S3_SettlementWithAJoiningHouse_AndTheControlRowMatches()
    {
        await _participations.UpsertAsync(new Participation { Id = "vE", ComponentId = "vodarna", HouseId = "E", ValidFrom = D(2026, 10, 1) });
        await _entries.UpsertAsync(new CostEntry
        {
            Id = "s", ComponentId = "vodarna", Type = CostEntryType.Settlement, PeriodFrom = D(2026, 7, 1), PeriodTo = D(2026, 12, 31), Amount = 1_840m,
        });

        var overview = await Sut().GetOverviewAsync(D(2026, 7, 1), D(2026, 12, 31));

        overview.Houses.Select(h => (h.HouseName, h.Costs.GetValueOrDefault("vodarna"))).Should().Equal(
            ("RD A", 414m), ("RD B", 414m), ("RD C", 414m), ("RD D", 414m), ("RD E", 184m), ("RD F", 0m));
        overview.Houses.Select(h => h.Saldo).Should().Equal(-414m, -414m, -414m, -414m, -184m, 0m);
        var control = overview.Components.Single(c => c.ComponentId == "vodarna");
        control.AllocatedTotal.Should().Be(1_840m);
        control.HousesTotal.Should().Be(1_840m);
        control.Matches.Should().BeTrue();
    }

    [Fact]
    public async Task EntryAcrossTheRangeBoundaryCountsOnlyItsDaysInside()
    {
        await _entries.UpsertAsync(new CostEntry { Id = "q", ComponentId = "vodarna", Type = CostEntryType.Advance, PeriodFrom = D(2024, 1, 1), PeriodTo = D(2024, 3, 31), Amount = 910m });

        var overview = await Sut().GetOverviewAsync(D(2024, 2, 1), D(2024, 2, 29));

        overview.Components.Single().AllocatedTotal.Should().Be(290m); // 29 of 91 days
        overview.Houses.Where(h => h.Costs.ContainsKey("vodarna")).Sum(h => h.Costs["vodarna"]).Should().Be(290m);
    }

    [Fact]
    public async Task PaymentsFundShareAndPayoutsBuildTheRunningSaldo()
    {
        await _openings.UpsertAsync(new OpeningBalance { Key = "FundShare|A|x", Type = OpeningBalanceType.FundShare, HouseId = "A", OwnershipPeriodId = OwnershipPeriod.KeyFor("A", Start), Date = Start, Value = 1_000m, Source = "závěrka 2022" });
        await _payments.UpsertAsync(new AdvancePayment { HouseId = "A", RowKey = "2023-11", Type = PaymentType.Advance, Year = 2023, Month = 11, Amount = 1_500m });
        await _payments.UpsertAsync(new AdvancePayment { HouseId = "A", RowKey = "D-1", Type = PaymentType.Doplatek, PaymentDate = new DateTime(2023, 12, 10), Amount = 300m, Note = "z banky" });
        await _payments.UpsertAsync(new AdvancePayment { HouseId = "A", RowKey = "V-1", Type = PaymentType.Payout, PaymentDate = new DateTime(2023, 12, 20), Amount = 200m });
        await _payments.UpsertAsync(new AdvancePayment { HouseId = "A", RowKey = "O-1", Type = PaymentType.OpeningBalance, PaymentDate = new DateTime(2023, 11, 1), Amount = -9_999m });
        Advance("vodarna", D(2023, 11, 1), 400m, PaidFrom.Bank);

        var ledger = await Sut().GetHouseLedgerAsync("A", null, null, null, Admin);

        ledger.From.Should().Be(Start);
        ledger.To.Should().Be(D(2023, 12, 31));
        ledger.Items.Select(i => (i.Kind, i.Amount)).Should().Equal(
            (LedgerItemKind.Opening, 1_000m), (LedgerItemKind.Payment, 1_500m), (LedgerItemKind.Cost, -100m),
            (LedgerItemKind.Payment, 300m), (LedgerItemKind.Payout, -200m));
        ledger.Opening.Should().Be(1_000m);
        ledger.Payments.Should().Be(1_600m);
        ledger.Costs.Should().Be(100m);
        ledger.Saldo.Should().Be(2_500m);
        ledger.Items[3].Description.Should().Be("Doplatek — z banky");
    }

    [Fact]
    public async Task S4_NewOwnerStartsFresh_CostsFromTheSaleDayGoToTheNewPeriod()
    {
        var sale = D(2026, 3, 15);
        var old = _periods.Items.Values.Single(p => p.HouseId == "B");
        old.ValidTo = sale.AddDays(-1);
        await _periods.UpsertAsync(new OwnershipPeriod { Id = OwnershipPeriod.KeyFor("B", sale), HouseId = "B", OwnerName = "Nový vlastník", ValidFrom = sale });
        await _openings.UpsertAsync(new OpeningBalance { Key = "FundShare|B|old", Type = OpeningBalanceType.FundShare, HouseId = "B", OwnershipPeriodId = old.Id, Date = Start, Value = 700m, Source = "závěrka" });
        await _openings.UpsertAsync(new OpeningBalance { Key = "FundShare|B|new", Type = OpeningBalanceType.FundShare, HouseId = "B", OwnershipPeriodId = OwnershipPeriod.KeyFor("B", sale), Date = sale, Value = 0m, Source = "převod domu" });
        Advance("vodarna", D(2026, 3, 1), 3_100m, PaidFrom.Bank);

        var current = await Sut().GetHouseLedgerAsync("B", null, D(2026, 1, 1), D(2026, 3, 31), Admin);
        var previous = await Sut().GetHouseLedgerAsync("B", old.Id, D(2026, 1, 1), D(2026, 3, 31), Admin);

        current.OwnershipPeriod!.OwnerName.Should().Be("Nový vlastník");
        current.From.Should().Be(sale);
        current.Opening.Should().Be(0m);
        current.Costs.Should().Be(425m);  // 17 of 31 days of 3 100 Kč = 1 700 / 4
        previous.To.Should().Be(D(2026, 3, 14));
        previous.Costs.Should().Be(350m); // 14 of 31 days = 1 400 / 4
        previous.OwnershipPeriods.Should().HaveCount(2);
    }

    [Fact]
    public async Task WaterAndLossesComeIntoTheLedgerWithTheirCalculation()
    {
        await _components.UpsertAsync(new CostComponent { Id = "pvk", Name = "Voda PVK", Code = "VODA_PVK", StartDate = Start, AllocationBasis = AllocationBasis.Metered, WaterRole = WaterRole.Consumption });
        await _components.UpsertAsync(new CostComponent { Id = "ztraty", Name = "Ztráty vody", Code = "ZTRATY_VODY", StartDate = Start, AllocationBasis = AllocationBasis.Metered, WaterRole = WaterRole.Losses });
        await _rules.UpsertAsync(new ComponentAllocationRule { Id = "rz", ComponentId = "ztraty", ValidFrom = Start, Method = AllocationMethod.Ratio, RatioSource = "VODA_PVK" });
        await _meters.UpsertAsync(new WaterMeter { Id = "main", MeterNumber = "H", Type = MeterType.Main });
        void Read(string meter, DateOnly day, decimal value) =>
            _readings.UpsertAsync(new MeterReading { MeterId = meter, ReadingDate = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Value = value });
        Read("main", D(2026, 1, 1), 0m);
        Read("main", D(2026, 7, 1), 100m);
        foreach (var (house, m3) in new[] { ("A", 40m), ("B", 30m), ("C", 15m), ("D", 5m) })
        {
            await _meters.UpsertAsync(new WaterMeter { Id = $"m{house}", MeterNumber = house, Type = MeterType.Individual, HouseId = house });
            Read($"m{house}", D(2026, 1, 1), 0m);
            Read($"m{house}", D(2026, 7, 1), m3);
            await _participations.UpsertAsync(new Participation { Id = $"w{house}", ComponentId = "pvk", HouseId = house, ValidFrom = Start });
            await _participations.UpsertAsync(new Participation { Id = $"l{house}", ComponentId = "ztraty", HouseId = house, ValidFrom = Start });
        }
        await _entries.UpsertAsync(new CostEntry { Id = "f", ComponentId = "pvk", Type = CostEntryType.OneOff, PeriodFrom = D(2026, 1, 1), PeriodTo = D(2026, 6, 30), Amount = 10_000m, QuantityM3 = 100m });

        var c = await Sut().GetHouseLedgerAsync("C", null, D(2026, 1, 1), D(2026, 6, 30), Admin);
        var overview = await Sut().GetOverviewAsync(D(2026, 1, 1), D(2026, 6, 30));

        c.Items.Select(i => (i.Kind, i.Amount)).Should().Equal((LedgerItemKind.Water, -1_500m), (LedgerItemKind.Loss, -166.67m));
        c.Items[0].Detail!.Explanation.Should().Contain("15 m³").And.Contain("100,00 Kč/m³");
        c.Items[1].Detail!.Explanation.Should().Contain("Ztráta 10 m³").And.Contain("poměrem");
        overview.Components.Should().Contain(x => x.ComponentId == "pvk" && x.AllocatedTotal == 9_000m && x.Matches);
        overview.Components.Should().Contain(x => x.ComponentId == "ztraty" && x.AllocatedTotal == 1_000m && x.Matches);
    }

    [Fact]
    public async Task MemberSeesOnlyTheirOwnHouseDetail()
    {
        var member = new LedgerRequester(UserRole.Member, "A");

        (await Sut().GetHouseLedgerAsync("A", null, null, null, member)).HouseName.Should().Be("RD A");
        var other = () => Sut().GetHouseLedgerAsync("B", null, null, null, member);
        (await other.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(403);

        var accountant = new LedgerRequester(UserRole.Accountant, null);
        (await Sut().GetHouseLedgerAsync("B", null, null, null, accountant)).HouseName.Should().Be("RD B");
    }

    [Fact]
    public async Task ProblemsAreReportedNotHidden()
    {
        await _components.UpsertAsync(new CostComponent { Id = "jimka", Name = "Odvoz jímky", Code = "JIMKA", StartDate = Start, AllocationBasis = AllocationBasis.CostEntries });
        await _rules.UpsertAsync(new ComponentAllocationRule { Id = "rj", ComponentId = "jimka", ValidFrom = Start, Method = AllocationMethod.Equal });
        await _entries.UpsertAsync(new CostEntry { Id = "j", ComponentId = "jimka", Type = CostEntryType.OneOff, PeriodFrom = D(2023, 12, 5), PeriodTo = D(2023, 12, 5), Amount = 3_000m });

        var overview = await Sut().GetOverviewAsync(null, null);

        overview.Components.Single(c => c.ComponentId == "jimka").Warnings.Should().ContainSingle().Which.Should().Contain("žádný dům");
        var missing = () => Sut().GetHouseLedgerAsync("Z", null, null, null, Admin);
        await missing.Should().ThrowAsync<NotFoundException>();
        var badPeriod = () => Sut().GetHouseLedgerAsync("A", "nope", null, null, Admin);
        await badPeriod.Should().ThrowAsync<NotFoundException>();
        var reversed = () => Sut().GetOverviewAsync(D(2024, 2, 1), D(2024, 1, 1));
        await reversed.Should().ThrowAsync<AppException>();
    }
}
