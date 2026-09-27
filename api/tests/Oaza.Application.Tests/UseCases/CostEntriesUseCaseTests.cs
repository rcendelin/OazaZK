using FluentAssertions;
using Moq;
using Oaza.Application.Audit;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Tests.TestSupport;
using Oaza.Application.UseCases;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Domain.Services;

namespace Oaza.Application.Tests.UseCases;

public class CostEntriesUseCaseTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private sealed class MemoryEntries() : MemoryRepo<CostEntry>(e => e.ComponentId, e => e.Id), ICostEntryRepository
    {
        public Task<IReadOnlyList<CostEntry>> GetByComponentAsync(string componentId) => GetByPartitionKeyAsync(componentId);
    }

    private readonly MemoryEntries _entries = new();
    private readonly MemoryComponents _components = new();
    private readonly MemoryRules _rules = new();
    private readonly MemoryParticipations _participations = new();
    private readonly MemoryHouses _houses = new();
    private readonly ClosedUntil _closed = new(null);
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly AuditActor _actor = new("admin-1", "Rosťa");

    public CostEntriesUseCaseTests()
    {
        var start = D(2023, 11, 1);
        foreach (var name in new[] { "A", "B", "C", "D", "E", "F" })
            _houses.UpsertAsync(new House { Id = name, Name = $"RD {name}" });

        _components.UpsertAsync(new CostComponent { Id = "vodarna", Name = "Elektřina – vodárna", Code = "ELEKTRINA_VODARNA", StartDate = start, AllocationBasis = AllocationBasis.CostEntries });
        _rules.UpsertAsync(new ComponentAllocationRule { Id = "r1", ComponentId = "vodarna", ValidFrom = start, Method = AllocationMethod.Equal });
        foreach (var house in new[] { "A", "B", "C", "D" })
            _participations.UpsertAsync(new Participation { Id = house, ComponentId = "vodarna", HouseId = house, ValidFrom = start });

        _components.UpsertAsync(new CostComponent { Id = "pvk", Name = "Voda PVK", Code = "VODA_PVK", StartDate = start, AllocationBasis = AllocationBasis.Metered });
        _components.UpsertAsync(new CostComponent { Id = "jimka", Name = "Odvoz jímky", Code = "JIMKA", StartDate = start, AllocationBasis = AllocationBasis.CostEntries });
        _rules.UpsertAsync(new ComponentAllocationRule { Id = "r3", ComponentId = "jimka", ValidFrom = start, Method = AllocationMethod.Equal });
    }

    private CostEntriesUseCase Sut() => new(_entries, _components, _rules, _participations, _houses, _closed, _audit.Object);

    private static SaveCostEntryRequest Advance(decimal amount, DateOnly from, DateOnly to, PaidFrom paidFrom = PaidFrom.Bank) => new()
    {
        Type = CostEntryType.Advance, PeriodFrom = from, PeriodTo = to, Amount = amount, Supplier = "PRE", PaidFrom = paidFrom,
    };

    [Fact]
    public async Task S2_AdvancePaidFromSupplierCreditIsAllocatedLikeOnePaidFromBank()
    {
        var fromCredit = await Sut().CreateAsync("vodarna", Advance(500m, D(2023, 11, 1), D(2023, 11, 30), PaidFrom.SupplierCredit), _actor);
        var fromBank = await Sut().CreateAsync("vodarna", Advance(500m, D(2023, 12, 1), D(2023, 12, 31), PaidFrom.Bank), _actor);

        var a = await Sut().GetAllocationAsync("vodarna", fromCredit.Id);
        var b = await Sut().GetAllocationAsync("vodarna", fromBank.Id);

        a.HouseTotals.Select(s => (s.HouseName, s.Amount)).Should().Equal(("RD A", 125m), ("RD B", 125m), ("RD C", 125m), ("RD D", 125m));
        b.HouseTotals.Select(s => s.Amount).Should().Equal(a.HouseTotals.Select(s => s.Amount));
        a.Entry.PaidFrom.Should().Be(PaidFrom.SupplierCredit);
        a.Segments.Should().ContainSingle().Which.Amount.Should().Be(500m);
        _audit.Verify(x => x.LogAsync(CostEntriesUseCase.CostEntryEntity, fromCredit.Id, AuditActions.Create, null, It.IsAny<object>(), _actor, null), Times.Once);
    }

    [Fact]
    public async Task RecurringMonthlyAdvanceGeneratesTheRightNumberOfEntries()
    {
        var created = await Sut().CreateRecurringAsync("vodarna", new RecurringAdvanceRequest
        {
            Amount = 500m, Periodicity = AdvancePeriodicity.Monthly, From = D(2023, 11, 1), To = D(2024, 10, 31),
            Supplier = "PRE", PaidFrom = PaidFrom.SupplierCredit,
        }, _actor);

        created.Should().HaveCount(12);
        _entries.Items.Should().HaveCount(12);
        _entries.Items.Values.Should().OnlyContain(e => e.Type == CostEntryType.Advance && e.Amount == 500m && e.PaidFrom == PaidFrom.SupplierCredit);
        (await Sut().ListAsync("vodarna", D(2024, 1, 1), D(2024, 3, 31))).Should().HaveCount(3);
    }

    [Fact]
    public async Task RecurringSeriesIsValidatedBeforeAnythingIsWritten()
    {
        var beforeStart = () => Sut().CreateRecurringAsync("vodarna", new RecurringAdvanceRequest
        {
            Amount = 500m, From = D(2023, 9, 1), To = D(2023, 12, 31),
        }, _actor);
        await beforeStart.Should().ThrowAsync<BusinessRuleException>();

        var reversed = () => Sut().CreateRecurringAsync("vodarna", new RecurringAdvanceRequest { Amount = 1m, From = D(2024, 2, 1), To = D(2024, 1, 1) }, _actor);
        (await reversed.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().NotContain("Parameter");
        _entries.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task S3_SettlementAcrossAJoiningHouse()
    {
        await _participations.UpsertAsync(new Participation { Id = "E", ComponentId = "vodarna", HouseId = "E", ValidFrom = D(2026, 10, 1) });

        var created = await Sut().CreateAsync("vodarna", new SaveCostEntryRequest
        {
            Type = CostEntryType.Settlement, PeriodFrom = D(2026, 7, 1), PeriodTo = D(2026, 12, 31), Amount = 1_840m, PaidFrom = PaidFrom.Bank,
        }, _actor);
        var allocation = await Sut().GetAllocationAsync("vodarna", created.Id);

        allocation.HouseTotals.Select(s => (s.HouseName, s.Amount)).Should().Equal(
            ("RD A", 414m), ("RD B", 414m), ("RD C", 414m), ("RD D", 414m), ("RD E", 184m));
        allocation.Segments.Should().HaveCount(6); // month cuts
        allocation.Segments.Sum(s => s.Amount).Should().Be(1_840m);
    }

    [Fact]
    public async Task InvalidEntriesAreRejected()
    {
        var reversed = () => Sut().CreateAsync("vodarna", Advance(1m, D(2024, 2, 1), D(2024, 1, 1)), _actor);
        await reversed.Should().ThrowAsync<BusinessRuleException>();
        var early = () => Sut().CreateAsync("vodarna", Advance(1m, D(2023, 10, 1), D(2023, 10, 31)), _actor);
        (await early.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("R3");
        var negativeAdvance = () => Sut().CreateAsync("vodarna", Advance(-1m, D(2024, 1, 1), D(2024, 1, 31)), _actor);
        await negativeAdvance.Should().ThrowAsync<BusinessRuleException>();
        var zero = () => Sut().CreateAsync("vodarna", new SaveCostEntryRequest { Type = CostEntryType.OneOff, PeriodFrom = D(2024, 1, 1), PeriodTo = D(2024, 1, 1) }, _actor);
        await zero.Should().ThrowAsync<BusinessRuleException>();
        var cents = () => Sut().CreateAsync("vodarna", Advance(1.001m, D(2024, 1, 1), D(2024, 1, 31)), _actor);
        await cents.Should().ThrowAsync<BusinessRuleException>();
        var noDates = () => Sut().CreateAsync("vodarna", new SaveCostEntryRequest { Type = CostEntryType.OneOff, Amount = 1m }, _actor);
        await noDates.Should().ThrowAsync<BusinessRuleException>();
        var quantityOnNonMetered = Advance(1m, D(2024, 1, 1), D(2024, 1, 31));
        quantityOnNonMetered.QuantityM3 = 5m;
        var qty = () => Sut().CreateAsync("vodarna", quantityOnNonMetered, _actor);
        await qty.Should().ThrowAsync<BusinessRuleException>();
        var nobody = () => Sut().CreateAsync("jimka", new SaveCostEntryRequest { Type = CostEntryType.OneOff, PeriodFrom = D(2024, 5, 1), PeriodTo = D(2024, 5, 1), Amount = 3_000m }, _actor);
        (await nobody.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("žádný dům");
        var missing = () => Sut().CreateAsync("nope", Advance(1m, D(2024, 1, 1), D(2024, 1, 31)), _actor);
        await missing.Should().ThrowAsync<NotFoundException>();
        _entries.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task MeteredComponentInvoicesNeedQuantity_AndAreNotAllocatedDirectly()
    {
        var noQuantity = () => Sut().CreateAsync("pvk", new SaveCostEntryRequest { Type = CostEntryType.OneOff, PeriodFrom = D(2024, 1, 1), PeriodTo = D(2024, 1, 31), Amount = 4_500m }, _actor);
        (await noQuantity.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("m³");

        var invoice = await Sut().CreateAsync("pvk", new SaveCostEntryRequest
        {
            Type = CostEntryType.OneOff, PeriodFrom = D(2024, 1, 1), PeriodTo = D(2024, 1, 31), Amount = 4_500m, QuantityM3 = 50m, Supplier = "PVK",
        }, _actor);
        invoice.QuantityM3.Should().Be(50m);

        var allocation = () => Sut().GetAllocationAsync("pvk", invoice.Id);
        (await allocation.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("odečtů");
    }

    [Fact]
    public async Task UpdateAndDeleteAreAudited_AndBlockedAfterAClosing()
    {
        var created = await Sut().CreateAsync("vodarna", Advance(500m, D(2024, 1, 1), D(2024, 1, 31)), _actor);

        var request = Advance(600m, D(2024, 1, 1), D(2024, 1, 31));
        request.Reason = "překlep";
        var updated = await Sut().UpdateAsync("vodarna", created.Id, request, _actor);
        updated.Amount.Should().Be(600m);
        _audit.Verify(x => x.LogAsync(CostEntriesUseCase.CostEntryEntity, created.Id, AuditActions.Update,
            It.Is<object>(o => ((CostEntry)o).Amount == 500m), It.Is<object>(o => ((CostEntry)o).Amount == 600m), _actor, "překlep"), Times.Once);

        _closed.Day = D(2024, 1, 31);
        (await Sut().ListAsync("vodarna", null, null)).Single().Locked.Should().BeTrue();
        var update = () => Sut().UpdateAsync("vodarna", created.Id, Advance(700m, D(2024, 1, 1), D(2024, 1, 31)), _actor);
        (await update.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("uzavřeného období");
        var delete = () => Sut().DeleteAsync("vodarna", created.Id, null, _actor);
        await delete.Should().ThrowAsync<BusinessRuleException>();

        _closed.Day = null;
        await Sut().DeleteAsync("vodarna", created.Id, "omyl", _actor);
        _entries.Items.Should().BeEmpty();
        _audit.Verify(x => x.LogAsync(CostEntriesUseCase.CostEntryEntity, created.Id, AuditActions.Delete, It.IsAny<object>(), null, _actor, "omyl"), Times.Once);

        var missingUpdate = () => Sut().UpdateAsync("vodarna", "nope", request, _actor);
        await missingUpdate.Should().ThrowAsync<NotFoundException>();
        var missingDelete = () => Sut().DeleteAsync("vodarna", "nope", null, _actor);
        await missingDelete.Should().ThrowAsync<NotFoundException>();
        var missingAllocation = () => Sut().GetAllocationAsync("vodarna", "nope");
        await missingAllocation.Should().ThrowAsync<NotFoundException>();
    }
}
