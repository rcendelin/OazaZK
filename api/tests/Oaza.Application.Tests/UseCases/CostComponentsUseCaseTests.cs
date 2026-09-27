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
using Oaza.Domain.Time;

namespace Oaza.Application.Tests.UseCases;

public class CostComponentsUseCaseTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private readonly MemoryComponents _components = new();
    private readonly MemoryRules _rules = new();
    private readonly MemoryParticipations _participations = new();
    private readonly MemoryHouses _houses = new();
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly AuditActor _actor = new("admin-1", "Rosťa");
    private DateOnly? _closedUntil;

    public CostComponentsUseCaseTests()
    {
        foreach (var name in new[] { "A", "B", "C", "D", "E", "F" })
            _houses.UpsertAsync(new House { Id = name, Name = $"RD {name}" });
    }

    // 27. 9. 2026 12:00 Prague
    private CostComponentsUseCase Sut() => new(
        _components, _rules, _participations, _houses, new ClosedUntil(_closedUntil), _audit.Object,
        new PragueClock(new FixedTimeProvider(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero))));

    private async Task<string> WaterworksAsync(AllocationMethod method = AllocationMethod.Equal)
    {
        var created = await Sut().CreateAsync(new CreateCostComponentRequest
        {
            Name = "Elektřina – vodárna", Code = "elektrina_vodarna", StartDate = D(2023, 11, 1),
            AllocationBasis = AllocationBasis.CostEntries, Method = method,
        }, _actor);
        return created.Id;
    }

    private Task AddAsync(string id, string house, DateOnly from, DateOnly? to = null, decimal? weight = null) =>
        Sut().AddParticipationAsync(id, new AddParticipationRequest { HouseId = house, ValidFrom = from, ValidTo = to, Weight = weight }, _actor);

    // ───────── create / update ─────────

    [Fact]
    public async Task Create_NormalizesCode_CreatesFirstRuleFromStart_AndAudits()
    {
        var id = await WaterworksAsync();

        var component = _components.Items.Values.Single();
        component.Code.Should().Be("ELEKTRINA_VODARNA");
        var rule = _rules.Items.Values.Single();
        rule.ValidFrom.Should().Be(D(2023, 11, 1));
        rule.ValidTo.Should().BeNull();
        rule.Method.Should().Be(AllocationMethod.Equal);
        _audit.Verify(a => a.LogAsync(CostComponentsUseCase.ComponentEntity, id, AuditActions.Create, null, It.IsAny<object>(), _actor, null), Times.Once);
        _audit.Verify(a => a.LogAsync(CostComponentsUseCase.RuleEntity, rule.Id, AuditActions.Create, null, It.IsAny<object>(), _actor, "Založení složky"), Times.Once);
    }

    [Fact]
    public async Task Create_MeteredComponentDefaultsToMeteredMethod()
    {
        await Sut().CreateAsync(new CreateCostComponentRequest
        {
            Name = "Voda PVK", Code = "VODA_PVK", StartDate = D(2023, 11, 1), AllocationBasis = AllocationBasis.Metered,
        }, _actor);

        _rules.Items.Values.Single().Method.Should().Be(AllocationMethod.Metered);
    }

    [Fact]
    public async Task Create_RejectsDuplicateCode_And_InvalidInput()
    {
        await WaterworksAsync();

        var duplicate = () => Sut().CreateAsync(new CreateCostComponentRequest { Name = "X", Code = "Elektrina_Vodarna", StartDate = D(2024, 1, 1) }, _actor);
        (await duplicate.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);

        var invalid = () => Sut().CreateAsync(new CreateCostComponentRequest { Name = " ", Code = "voda pvk", StartDate = default }, _actor);
        (await invalid.Should().ThrowAsync<BusinessRuleException>()).Which.Errors.Should().HaveCount(3);
    }

    [Fact]
    public async Task Update_ChangesNameActiveNote_AndAuditsOldAndNew()
    {
        var id = await WaterworksAsync();

        var result = await Sut().UpdateAsync(id, new UpdateCostComponentRequest { Name = "Vodárna", Active = false, Note = "  " }, _actor);

        result.Name.Should().Be("Vodárna");
        result.Active.Should().BeFalse();
        result.Note.Should().BeNull();
        _audit.Verify(a => a.LogAsync(CostComponentsUseCase.ComponentEntity, id, AuditActions.Update,
            It.Is<object>(o => ((CostComponent)o).Name == "Elektřina – vodárna"), It.Is<object>(o => ((CostComponent)o).Name == "Vodárna"), _actor, null), Times.Once);

        var empty = () => Sut().UpdateAsync(id, new UpdateCostComponentRequest { Name = "" }, _actor);
        await empty.Should().ThrowAsync<AppException>();
        var missing = () => Sut().UpdateAsync("nope", new UpdateCostComponentRequest { Name = "X" }, _actor);
        await missing.Should().ThrowAsync<NotFoundException>();
    }

    // ───────── participation ─────────

    [Fact]
    public async Task WaterworksWithFourHousesFrom1Nov2023_ShowsOneSegment()
    {
        var id = await WaterworksAsync();
        foreach (var house in new[] { "A", "B", "C", "D" })
            await AddAsync(id, house, D(2023, 11, 1));

        var segments = await Sut().GetSegmentsAsync(id, D(2023, 11, 1), D(2023, 12, 31));

        segments.Should().ContainSingle();
        segments[0].Days.Should().Be(61);
        segments[0].Method.Should().Be(AllocationMethod.Equal);
        segments[0].Participants.Select(p => p.HouseName).Should().Equal("RD A", "RD B", "RD C", "RD D");

        var list = await Sut().ListAsync();
        list.Single().CurrentParticipants.Should().Be(4);
        list.Single().CurrentMethod.Should().Be(AllocationMethod.Equal);
    }

    [Fact]
    public async Task OverlappingParticipationIsRejected_AndNothingIsSaved()
    {
        var id = await WaterworksAsync();
        await AddAsync(id, "A", D(2023, 11, 1));

        var act = () => AddAsync(id, "A", D(2024, 1, 1));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("překrývá");
        _participations.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task ParticipationBeforeStartOrUnknownHouseIsRejected()
    {
        var id = await WaterworksAsync();

        var early = () => AddAsync(id, "A", D(2023, 10, 31));
        (await early.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("před začátkem účtování");

        var unknown = () => AddAsync(id, "Z", D(2024, 1, 1));
        (await unknown.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task PercentNot100IsRejectedWithDateAndSum()
    {
        var id = await WaterworksAsync(AllocationMethod.Percent);

        var act = () => AddAsync(id, "A", D(2023, 11, 1), weight: 60);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message
            .Should().Be("K 1. 11. 2023 je součet procent účastníků 60 %, musí být 100 %.");
    }

    [Fact]
    public async Task EndParticipation_SetsLastDay_AndAudits()
    {
        var id = await WaterworksAsync();
        await AddAsync(id, "B", D(2023, 11, 1));
        var participation = _participations.Items.Values.Single();

        var result = await Sut().EndParticipationAsync(id, participation.Id, new EndParticipationRequest { ValidTo = D(2026, 3, 14), Reason = "prodej" }, _actor);

        result.ValidTo.Should().Be(D(2026, 3, 14));
        _audit.Verify(a => a.LogAsync(CostComponentsUseCase.ParticipationEntity, participation.Id, AuditActions.Update,
            It.Is<object>(o => ((Participation)o).ValidTo == null), It.Is<object>(o => ((Participation)o).ValidTo == D(2026, 3, 14)), _actor, "prodej"), Times.Once);

        var reversed = () => Sut().EndParticipationAsync(id, participation.Id, new EndParticipationRequest { ValidTo = D(2023, 10, 1) }, _actor);
        await reversed.Should().ThrowAsync<BusinessRuleException>();
        var missing = () => Sut().EndParticipationAsync(id, "nope", new EndParticipationRequest { ValidTo = D(2026, 1, 1) }, _actor);
        await missing.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task DeleteParticipation_RemovesAndAudits()
    {
        var id = await WaterworksAsync();
        await AddAsync(id, "E", D(2024, 1, 1));
        var participation = _participations.Items.Values.Single();

        await Sut().DeleteParticipationAsync(id, participation.Id, "omyl", _actor);

        _participations.Items.Should().BeEmpty();
        _audit.Verify(a => a.LogAsync(CostComponentsUseCase.ParticipationEntity, participation.Id, AuditActions.Delete, It.IsAny<object>(), null, _actor, "omyl"), Times.Once);
    }

    // ───────── rules ─────────

    [Fact]
    public async Task AddRule_EndsThePreviousRuleTheDayBefore_AndRequiresReason()
    {
        var id = await WaterworksAsync();

        var noReason = () => Sut().AddRuleAsync(id, new AddAllocationRuleRequest { ValidFrom = D(2026, 10, 1), Method = AllocationMethod.Ratio, Reason = " " }, _actor);
        (await noReason.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("důvod");

        await Sut().AddRuleAsync(id, new AddAllocationRuleRequest
        {
            ValidFrom = D(2026, 10, 1), Method = AllocationMethod.Ratio, RatioSource = "VODA_PVK", Reason = "hlasování schůze 10/2026",
        }, _actor);

        var detail = await Sut().GetDetailAsync(id);
        detail.Rules.Select(r => (r.ValidFrom, r.ValidTo, r.Method)).Should().Equal(
            (D(2023, 11, 1), D(2026, 9, 30), AllocationMethod.Equal),
            (D(2026, 10, 1), (DateOnly?)null, AllocationMethod.Ratio));
        detail.Rules[1].RatioSource.Should().Be("VODA_PVK");
        _audit.Verify(a => a.LogAsync(CostComponentsUseCase.RuleEntity, It.IsAny<string>(), AuditActions.Update, It.IsAny<object>(), It.IsAny<object>(), _actor, "hlasování schůze 10/2026"), Times.Once);

        // The method switch applies only from its effective date (O1).
        var segments = await Sut().GetSegmentsAsync(id, D(2026, 7, 1), D(2026, 12, 31));
        segments.Select(s => s.Method).Should().Equal(AllocationMethod.Equal, AllocationMethod.Ratio);
    }

    [Fact]
    public async Task AddRule_OnTheSameDayAsExistingRuleIsRejected()
    {
        var id = await WaterworksAsync();

        var act = () => Sut().AddRuleAsync(id, new AddAllocationRuleRequest { ValidFrom = D(2023, 11, 1), Method = AllocationMethod.Ratio, Reason = "x" }, _actor);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("překrývají");
    }

    [Fact]
    public async Task DeleteRule_ReopensThePreviousRule_ButNotTheOnlyOne()
    {
        var id = await WaterworksAsync();
        await Sut().AddRuleAsync(id, new AddAllocationRuleRequest { ValidFrom = D(2026, 10, 1), Method = AllocationMethod.Ratio, Reason = "x" }, _actor);
        var later = _rules.Items.Values.Single(r => r.ValidFrom == D(2026, 10, 1));

        await Sut().DeleteRuleAsync(id, later.Id, "omyl", _actor);

        var only = _rules.Items.Values.Single();
        only.ValidTo.Should().BeNull();
        var act = () => Sut().DeleteRuleAsync(id, only.Id, null, _actor);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("aspoň jedno pravidlo");
    }

    // ───────── closed period ─────────

    [Fact]
    public async Task ChangesIntoAClosedPeriodAreRejected()
    {
        var id = await WaterworksAsync();
        await AddAsync(id, "A", D(2023, 11, 1));
        var participation = _participations.Items.Values.Single();
        _closedUntil = D(2025, 12, 31);

        var add = () => AddAsync(id, "B", D(2025, 6, 1));
        (await add.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("mezizávěrka k 31. 12. 2025");

        var end = () => Sut().EndParticipationAsync(id, participation.Id, new EndParticipationRequest { ValidTo = D(2025, 6, 30) }, _actor);
        await end.Should().ThrowAsync<BusinessRuleException>();

        var delete = () => Sut().DeleteParticipationAsync(id, participation.Id, null, _actor);
        await delete.Should().ThrowAsync<BusinessRuleException>();

        var rule = () => Sut().AddRuleAsync(id, new AddAllocationRuleRequest { ValidFrom = D(2025, 1, 1), Method = AllocationMethod.Ratio, Reason = "x" }, _actor);
        await rule.Should().ThrowAsync<BusinessRuleException>();

        // After the closing it is fine.
        await AddAsync(id, "B", D(2026, 1, 1));
        await Sut().EndParticipationAsync(id, participation.Id, new EndParticipationRequest { ValidTo = D(2026, 3, 14) }, _actor);
        (await Sut().GetDetailAsync(id)).LastClosedDay.Should().Be(D(2025, 12, 31));
    }

    [Fact]
    public async Task SegmentsQueryValidatesRange()
    {
        var id = await WaterworksAsync();

        var reversed = () => Sut().GetSegmentsAsync(id, D(2026, 2, 1), D(2026, 1, 1));
        await reversed.Should().ThrowAsync<AppException>();
        var tooLong = () => Sut().GetSegmentsAsync(id, D(2000, 1, 1), D(2026, 1, 1));
        await tooLong.Should().ThrowAsync<AppException>();
        var missing = () => Sut().GetSegmentsAsync("nope", D(2026, 1, 1), D(2026, 1, 2));
        await missing.Should().ThrowAsync<NotFoundException>();
    }
}
