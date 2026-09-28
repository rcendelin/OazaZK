using FluentAssertions;
using Moq;
using Oaza.Application.Audit;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Interfaces;
using Oaza.Application.Ledger;
using Oaza.Application.Tests.TestSupport;
using Oaza.Application.UseCases;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Time;

namespace Oaza.Application.Tests.UseCases;

public class InterimClosingsUseCaseTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private static readonly DateOnly Start = D(2023, 11, 1);

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
    private readonly MemoryInterimClosings _closings = new();
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly AuditActor _actor = new("admin-1", "Rosťa");
    private readonly PragueClock _clock = new(new FixedTimeProvider(new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.Zero)));

    public InterimClosingsUseCaseTests()
    {
        foreach (var name in new[] { "A", "B", "C", "D", "E" })
            _houses.UpsertAsync(new House { Id = name, Name = $"RD {name}", IsActive = true });
        _components.UpsertAsync(new CostComponent { Id = "osvetleni", Name = "Osvětlení", Code = "OSVETLENI", StartDate = Start, AllocationBasis = AllocationBasis.CostEntries });
        _rules.UpsertAsync(new ComponentAllocationRule { Id = "r", ComponentId = "osvetleni", ValidFrom = Start, Method = AllocationMethod.Equal });
        foreach (var house in new[] { "A", "B", "C", "D" })
            _participations.UpsertAsync(new Participation { Id = house, ComponentId = "osvetleni", HouseId = house, ValidFrom = Start });
        _participations.UpsertAsync(new Participation { Id = "E", ComponentId = "osvetleni", HouseId = "E", ValidFrom = D(2026, 10, 1) });
    }

    private HouseLedgerUseCase Ledger() => new(
        new LedgerCostCollector(_components, _rules, _participations, _entries, _openings, _meters, _readings, _houses),
        _houses, _payments, _openings, _periods, _components, _clock);

    private InterimClosingsUseCase Sut() => new(_closings, _houses, Ledger(), _audit.Object, _clock);

    private CostEntriesUseCase Entries() => new(_entries, _components, _rules, _participations, _houses, new InterimClosingBoundary(_closings), _audit.Object);

    private Task<InterimClosingResponse> CloseAllAsync(DateOnly date) =>
        Sut().CreateAsync(new CreateInterimClosingRequest { Date = date, Scope = ClosingScope.All, Reason = "mezizávěrka" }, _actor);

    [Fact]
    public async Task S3_SettlementAcrossTheCutIsSplitByOriginalSegments_AndBookedIntoTheOpenPeriod()
    {
        await Entries().CreateAsync("osvetleni", new SaveCostEntryRequest { Type = CostEntryType.Advance, PeriodFrom = D(2026, 7, 1), PeriodTo = D(2026, 7, 31), Amount = 400m }, _actor);
        var closing = await CloseAllAsync(D(2026, 9, 30));
        closing.TotalSaldo.Should().Be(-400m);

        // The settlement for 1. 7.–31. 12. arrives after the closing.
        var noReason = () => Entries().CreateAsync("osvetleni", new SaveCostEntryRequest
        {
            Type = CostEntryType.Settlement, PeriodFrom = D(2026, 7, 1), PeriodTo = D(2026, 12, 31), Amount = 1_840m,
        }, _actor);
        (await noReason.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("opravný záznam k 1. 10. 2026");

        var settlement = await Entries().CreateAsync("osvetleni", new SaveCostEntryRequest
        {
            Type = CostEntryType.Settlement, PeriodFrom = D(2026, 7, 1), PeriodTo = D(2026, 12, 31), Amount = 1_840m, Reason = "vyúčtování PRE 2026",
        }, _actor);
        settlement.PostingDate.Should().Be(D(2026, 10, 1));

        var overview = await Ledger().GetOverviewAsync(D(2026, 10, 1), D(2026, 12, 31));
        overview.Houses.Select(h => (h.HouseName, h.Costs.GetValueOrDefault("osvetleni"))).Should().Equal(
            ("RD A", 414m), ("RD B", 414m), ("RD C", 414m), ("RD D", 414m), ("RD E", 184m));
        overview.Components.Single().Matches.Should().BeTrue();

        var a = await Ledger().GetHouseLedgerAsync("A", null, D(2026, 7, 1), D(2026, 12, 31), new LedgerRequester(UserRole.Admin, null));
        a.Items.Where(i => i.Date == D(2026, 10, 1)).Should().OnlyContain(i => i.Description.Contains("opravný záznam"));

        // The closed saldo did not move.
        var detail = await Sut().GetAsync(closing.Id);
        detail.Houses!.Should().OnlyContain(h => h.Difference == 0m);
    }

    [Fact]
    public async Task EntriesInTheClosedPeriodCannotBeChangedOrDeleted()
    {
        var entry = await Entries().CreateAsync("osvetleni", new SaveCostEntryRequest { Type = CostEntryType.Advance, PeriodFrom = D(2026, 7, 1), PeriodTo = D(2026, 7, 31), Amount = 400m }, _actor);
        await CloseAllAsync(D(2026, 9, 30));

        var update = () => Entries().UpdateAsync("osvetleni", entry.Id, new SaveCostEntryRequest { Type = CostEntryType.Advance, PeriodFrom = D(2026, 7, 1), PeriodTo = D(2026, 7, 31), Amount = 500m }, _actor);
        (await update.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("uzavřeného období");
        var delete = () => Entries().DeleteAsync("osvetleni", entry.Id, "x", _actor);
        await delete.Should().ThrowAsync<BusinessRuleException>();

        // Open-period entry cannot be moved back into the closed period.
        var open = await Entries().CreateAsync("osvetleni", new SaveCostEntryRequest { Type = CostEntryType.Advance, PeriodFrom = D(2026, 10, 1), PeriodTo = D(2026, 10, 31), Amount = 400m }, _actor);
        var moveBack = () => Entries().UpdateAsync("osvetleni", open.Id, new SaveCostEntryRequest { Type = CostEntryType.Advance, PeriodFrom = D(2026, 9, 1), PeriodTo = D(2026, 9, 30), Amount = 400m }, _actor);
        (await moveBack.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("opravný záznam");

        // Rules and participation before the cut are fixed too (T02).
        var components = new CostComponentsUseCase(_components, _rules, _participations, _houses, new InterimClosingBoundary(_closings), _audit.Object, _clock);
        var participation = () => components.AddParticipationAsync("osvetleni", new AddParticipationRequest { HouseId = "E", ValidFrom = D(2026, 9, 1), ValidTo = D(2026, 9, 30) }, _actor);
        (await participation.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("mezizávěrka k 30. 9. 2026");
    }

    [Fact]
    public async Task ClosingsMustMoveForward_HouseClosingsAreSeparate()
    {
        await CloseAllAsync(D(2026, 6, 30));

        var earlier = () => CloseAllAsync(D(2026, 6, 30));
        (await earlier.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("uzavřené do 30. 6. 2026");
        var future = () => CloseAllAsync(D(2027, 1, 15));
        (await future.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("včerejšku");
        var noReason = () => Sut().CreateAsync(new CreateInterimClosingRequest { Date = D(2026, 12, 31), Scope = ClosingScope.All }, _actor);
        await noReason.Should().ThrowAsync<BusinessRuleException>();
        var noHouse = () => Sut().CreateAsync(new CreateInterimClosingRequest { Date = D(2026, 12, 31), Scope = ClosingScope.House, Reason = "prodej" }, _actor);
        (await noHouse.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("Vyberte dům");

        // S4: house B sold on 15. 3. 2027 → closing of B at 14. 3. would be in the future here; take 14. 10. 2026.
        var house = await Sut().CreateAsync(new CreateInterimClosingRequest { Date = D(2026, 10, 14), Scope = ClosingScope.House, HouseId = "B", Reason = "prodej domu" }, _actor);
        house.HouseName.Should().Be("RD B");
        var boundary = new InterimClosingBoundary(_closings);
        (await boundary.GetLastClosedDayAsync("B")).Should().Be(D(2026, 10, 14));
        (await boundary.GetLastClosedDayAsync("A")).Should().Be(D(2026, 6, 30));
        (await boundary.GetLastClosedDayAsync()).Should().Be(D(2026, 10, 14));
        (await new NoClosingBoundary().GetLastClosedDayAsync("A")).Should().BeNull();

        var list = await Sut().ListAsync();
        list.Select(c => (c.Date, c.CanDelete)).Should().Equal((D(2026, 10, 14), true), (D(2026, 6, 30), false));
        _audit.Verify(a => a.LogAsync(InterimClosingsUseCase.InterimClosingEntity, It.IsAny<string>(), AuditActions.Create, null, It.IsAny<object>(), _actor, It.IsAny<string>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AnnualClosingExportsTheYearForTheAccountant()
    {
        await Entries().CreateAsync("osvetleni", new SaveCostEntryRequest { Type = CostEntryType.Advance, PeriodFrom = D(2026, 12, 1), PeriodTo = D(2026, 12, 31), Amount = 400m }, _actor);
        var annual = await CloseAllAsync(D(2026, 12, 31));
        var house = await Sut().CreateAsync(new CreateInterimClosingRequest { Date = D(2027, 1, 10), Scope = ClosingScope.House, HouseId = "A", Reason = "prodej" }, _actor);

        var xlsx = await Sut().ExportAsync(annual.Id, "xlsx");
        var csv = await Sut().ExportAsync(annual.Id, "csv");
        var houseCsv = await Sut().ExportAsync(house.Id, "csv");

        xlsx.FileName.Should().Be("rocni-zaverka-2026.xlsx");
        csv.FileName.Should().Be("rocni-zaverka-2026.csv");
        System.Text.Encoding.UTF8.GetString(csv.Content).Should().Contain("RD A;0,00;80,00");
        houseCsv.FileName.Should().StartWith("saldo-rd-a-");
        var missing = () => Sut().ExportAsync("nope", "csv");
        await missing.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task OnlyTheLatestClosingCanBeRemoved_WithAReason()
    {
        var first = await CloseAllAsync(D(2026, 6, 30));
        var second = await CloseAllAsync(D(2026, 9, 30));

        var older = () => Sut().DeleteAsync(first.Id, "omyl", _actor);
        (await older.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("jen poslední");
        var noReason = () => Sut().DeleteAsync(second.Id, " ", _actor);
        await noReason.Should().ThrowAsync<BusinessRuleException>();

        await Sut().DeleteAsync(second.Id, "špatné datum", _actor);

        _closings.Items.Should().ContainSingle();
        _audit.Verify(a => a.LogAsync(InterimClosingsUseCase.InterimClosingEntity, second.Id, AuditActions.Delete, It.IsAny<object>(), null, _actor, "špatné datum"), Times.Once);
        var missing = () => Sut().GetAsync("nope");
        await missing.Should().ThrowAsync<NotFoundException>();
        var missingDelete = () => Sut().DeleteAsync("nope", "x", _actor);
        await missingDelete.Should().ThrowAsync<NotFoundException>();
    }
}
