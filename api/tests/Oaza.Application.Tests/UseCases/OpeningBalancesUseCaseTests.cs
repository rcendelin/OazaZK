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

public class OpeningBalancesUseCaseTests
{
    private static readonly DateOnly Start = new(2023, 11, 1);

    private readonly MemoryOpeningBalances _balances = new();
    private readonly MemoryOwnershipPeriods _periods = new();
    private readonly MemoryHouses _houses = new();
    private readonly MemoryMeters _meters = new();
    private readonly MemoryReadings _readings = new();
    private readonly MemoryComponents _components = new();
    private readonly MemoryRules _rules = new();
    private readonly MemoryParticipations _participations = new();
    private readonly ClosedUntil _closed = new(null);
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly AuditActor _actor = new("admin-1", "Rosťa");

    public OpeningBalancesUseCaseTests()
    {
        foreach (var name in new[] { "A", "B", "C", "D", "E", "F" })
        {
            _houses.UpsertAsync(new House { Id = name, Name = $"RD {name}", ContactPerson = $"Vlastník {name}", Email = $"{name}@example.cz", IsActive = name != "F" });
            _meters.UpsertAsync(new WaterMeter { Id = $"m{name}", MeterNumber = $"V-{name}", HouseId = name, Type = MeterType.Individual });
        }
        _meters.UpsertAsync(new WaterMeter { Id = "main", MeterNumber = "HLAVNI", Type = MeterType.Main });

        _components.UpsertAsync(new CostComponent { Id = "vodarna", Name = "Elektřina – vodárna", Code = "ELEKTRINA_VODARNA", StartDate = Start });
        _rules.UpsertAsync(new ComponentAllocationRule { Id = "r1", ComponentId = "vodarna", ValidFrom = Start, Method = AllocationMethod.Equal });
        foreach (var house in new[] { "A", "B", "C", "D" })
            _participations.UpsertAsync(new Participation { Id = house, ComponentId = "vodarna", HouseId = house, ValidFrom = Start });

        _components.UpsertAsync(new CostComponent { Id = "pvk", Name = "Voda PVK", Code = "VODA_PVK", StartDate = Start, AllocationBasis = AllocationBasis.Metered });
        _rules.UpsertAsync(new ComponentAllocationRule { Id = "r2", ComponentId = "pvk", ValidFrom = Start, Method = AllocationMethod.Metered });
        _participations.UpsertAsync(new Participation { Id = "pA", ComponentId = "pvk", HouseId = "A", ValidFrom = Start });
    }

    private OpeningBalancesUseCase Sut() => new(
        _balances, _periods, _houses, _meters, _readings, _components, _rules, _participations, _closed, _audit.Object,
        new PragueClock(new FixedTimeProvider(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero))));

    private Task StartAsync() => Sut().StartOwnershipAsync(new StartOwnershipRequest { StartDate = Start }, _actor);

    private static SaveOpeningBalanceRequest Meter(string meterId, decimal value, bool estimate = false) => new()
    {
        Type = OpeningBalanceType.MeterReading, MeterId = meterId, Date = Start, Value = value, IsEstimate = estimate,
        Source = "odečet 22. 5. 2023, foto Jindra",
    };

    private static SaveOpeningBalanceRequest Fund(string houseId, decimal value) => new()
    {
        Type = OpeningBalanceType.FundShare, HouseId = houseId, Date = Start, Value = value, Source = "roční závěrka 2022",
    };

    // ───────── ownership ─────────

    [Fact]
    public async Task StartOwnership_CreatesOnePeriodPerActiveHouse_AndIsIdempotent()
    {
        var first = await Sut().StartOwnershipAsync(new StartOwnershipRequest { StartDate = Start }, _actor);
        var second = await Sut().StartOwnershipAsync(new StartOwnershipRequest { StartDate = new DateOnly(2024, 1, 1) }, _actor);

        first.Created.Should().Be(5); // F is inactive
        second.Created.Should().Be(0);
        second.Periods.Should().HaveCount(5);
        var a = _periods.Items.Values.Single(p => p.HouseId == "A");
        a.Id.Should().Be("A|2023-11-01");
        a.OwnerName.Should().Be("Vlastník A");
        a.Contact.Should().Be("A@example.cz");
        a.ValidTo.Should().BeNull();
        (await Sut().GetOwnershipPeriodsAsync()).Should().HaveCount(5);
        _audit.Verify(x => x.LogAsync(OpeningBalancesUseCase.OwnershipPeriodEntity, It.IsAny<string>(), AuditActions.Create, null, It.IsAny<object>(), _actor, "Start účtování"), Times.Exactly(5));

        var noDate = () => Sut().StartOwnershipAsync(new StartOwnershipRequest(), _actor);
        await noDate.Should().ThrowAsync<AppException>();
    }

    // ───────── fund share ─────────

    [Fact]
    public async Task FundShare_RequiresOwnershipPeriodAndSource()
    {
        var noPeriod = () => Sut().CreateAsync(Fund("A", 1500m), _actor);
        (await noPeriod.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("Start účtování");

        await StartAsync();
        var request = Fund("A", 1500m);
        request.Source = " ";
        var missingSource = () => Sut().CreateAsync(request, _actor);
        (await missingSource.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("zdroj");

        var created = await Sut().CreateAsync(Fund("A", 1500.50m), _actor);
        created.Key.Should().Be("FundShare|A|A|2023-11-01");
        created.OwnershipPeriodId.Should().Be("A|2023-11-01");
        created.HouseName.Should().Be("RD A");
        created.Locked.Should().BeFalse();
        _audit.Verify(x => x.LogAsync(OpeningBalancesUseCase.OpeningBalanceEntity, created.Key, AuditActions.Create, null, It.IsAny<object>(), _actor, null), Times.Once);
    }

    [Fact]
    public async Task SecondValueForTheSameCombinationIsRejected_UpdateInstead()
    {
        await StartAsync();
        var created = await Sut().CreateAsync(Fund("B", 100m), _actor);

        var duplicate = () => Sut().CreateAsync(Fund("B", 200m), _actor);
        (await duplicate.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);

        var updated = await Sut().UpdateAsync(created.Key, Fund("B", 200m), _actor);
        updated.Value.Should().Be(200m);
        _audit.Verify(x => x.LogAsync(OpeningBalancesUseCase.OpeningBalanceEntity, created.Key, AuditActions.Update, It.IsAny<object>(), It.IsAny<object>(), _actor, null), Times.Once);

        var otherTarget = () => Sut().UpdateAsync(created.Key, Fund("C", 1m), _actor);
        await otherTarget.Should().ThrowAsync<AppException>();
        var missing = () => Sut().UpdateAsync("nope", Fund("B", 1m), _actor);
        await missing.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task InvalidTargetsAndAmountsAreRejected()
    {
        await StartAsync();
        var noHouse = () => Sut().CreateAsync(Fund("Z", 1m), _actor);
        await noHouse.Should().ThrowAsync<BusinessRuleException>();
        var cents = () => Sut().CreateAsync(Fund("A", 1.005m), _actor);
        await cents.Should().ThrowAsync<BusinessRuleException>();
        var noMeter = () => Sut().CreateAsync(Meter("zzz", 1m), _actor);
        await noMeter.Should().ThrowAsync<BusinessRuleException>();
        var negative = () => Sut().CreateAsync(Meter("mA", -1m), _actor);
        await negative.Should().ThrowAsync<BusinessRuleException>();
        var litres = () => Sut().CreateAsync(Meter("mA", 1.0005m), _actor);
        await litres.Should().ThrowAsync<BusinessRuleException>();
        var noDate = Fund("A", 1m);
        noDate.Date = default;
        var missingDate = () => Sut().CreateAsync(noDate, _actor);
        await missingDate.Should().ThrowAsync<BusinessRuleException>();
        var noComponent = () => Sut().CreateAsync(new SaveOpeningBalanceRequest { Type = OpeningBalanceType.ComponentCredit, ComponentId = "x", Date = Start, Value = 1, Source = "s" }, _actor);
        await noComponent.Should().ThrowAsync<BusinessRuleException>();
        var badType = () => Sut().CreateAsync(new SaveOpeningBalanceRequest { Type = (OpeningBalanceType)99, Date = Start, Source = "s" }, _actor);
        await badType.Should().ThrowAsync<BusinessRuleException>();
    }

    // ───────── meter ─────────

    [Fact]
    public async Task MeterOpeningValue_BecomesAnEstimatedReadingOnThatDay()
    {
        await StartAsync();

        var created = await Sut().CreateAsync(Meter("mA", 260.855m, estimate: true), _actor);

        created.HouseId.Should().Be("A");
        created.MeterNumber.Should().Be("V-A");
        var reading = _readings.Items.Values.Single();
        reading.MeterId.Should().Be("mA");
        reading.ReadingDate.Should().Be(new DateTime(2023, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        reading.Value.Should().Be(260.855m);
        reading.IsEstimate.Should().BeTrue();
        reading.EstimateNote.Should().Be("Počáteční stav: odečet 22. 5. 2023, foto Jindra");
        reading.ImportedBy.Should().Be("admin-1");

        // Changing the opening value updates the reading it created.
        await Sut().UpdateAsync(created.Key, Meter("mA", 261m, estimate: false), _actor);
        _readings.Items.Values.Single().Value.Should().Be(261m);
        _readings.Items.Values.Single().IsEstimate.Should().BeFalse();
    }

    [Fact]
    public async Task MeterOpeningValue_UsesAnExistingReading_ButDoesNotOverwriteADifferentOne()
    {
        await StartAsync();
        await _readings.UpsertAsync(new MeterReading { MeterId = "mB", ReadingDate = new DateTime(2023, 11, 1, 0, 0, 0, DateTimeKind.Utc), Value = 100m });

        await Sut().CreateAsync(Meter("mB", 100m), _actor);
        _readings.Items.Should().HaveCount(1);

        var conflicting = () => Sut().CreateAsync(Meter("mC", 5m), _actor);
        await conflicting.Should().NotThrowAsync(); // no reading for C yet
        await _readings.UpsertAsync(new MeterReading { MeterId = "mD", ReadingDate = new DateTime(2023, 11, 1, 0, 0, 0, DateTimeKind.Utc), Value = 50m });
        var different = () => Sut().CreateAsync(Meter("mD", 51m), _actor);
        (await different.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("už existuje odečet 50 m³");
    }

    [Fact]
    public async Task MainMeterOpeningValueHasNoHouseOrPeriod()
    {
        var created = await Sut().CreateAsync(Meter("main", 5000m), _actor);

        created.Key.Should().Be("MeterReading|main|-");
        created.HouseId.Should().BeNull();
    }

    // ───────── component credit ─────────

    [Fact]
    public async Task S2_ComponentCreditIsStored_AndPreviewSplitsItEquallyAmongFourHouses()
    {
        var created = await Sut().CreateAsync(new SaveOpeningBalanceRequest
        {
            Type = OpeningBalanceType.ComponentCredit, ComponentId = "vodarna", Date = Start, Value = -20_000m, Source = "vyúčtování PRE 10/2023",
        }, _actor);
        created.Key.Should().Be("ComponentCredit|vodarna|-");
        created.ComponentName.Should().Be("Elektřina – vodárna");

        var preview = await Sut().PreviewComponentCreditAsync("vodarna", Start, -20_000m);

        preview.Method.Should().Be(AllocationMethod.Equal);
        preview.Shares.Select(s => (s.HouseName, s.Amount)).Should().Equal(
            ("RD A", -5_000m), ("RD B", -5_000m), ("RD C", -5_000m), ("RD D", -5_000m));
        preview.Shares.Sum(s => s.Amount).Should().Be(-20_000m);

        (await Sut().ListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task CreditPreviewRejectsMeteredComponentsAndBadInput()
    {
        var metered = () => Sut().PreviewComponentCreditAsync("pvk", Start, -100m);
        (await metered.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("z odečtů");

        var beforeStart = () => Sut().PreviewComponentCreditAsync("vodarna", new DateOnly(2023, 10, 1), -100m);
        await beforeStart.Should().ThrowAsync<BusinessRuleException>();

        var cents = () => Sut().PreviewComponentCreditAsync("vodarna", Start, -0.001m);
        await cents.Should().ThrowAsync<AppException>();

        var missing = () => Sut().PreviewComponentCreditAsync("nope", Start, 1m);
        await missing.Should().ThrowAsync<NotFoundException>();
    }

    // ───────── lock after interim closing ─────────

    [Fact]
    public async Task AfterAnInterimClosing_ChangesAreCorrectionsWithReason_AndDeleteIsBlocked()
    {
        await StartAsync();
        var created = await Sut().CreateAsync(Fund("D", 300m), _actor);
        _closed.Day = new DateOnly(2023, 12, 31);

        (await Sut().ListAsync()).Single().Locked.Should().BeTrue();

        var noReason = () => Sut().UpdateAsync(created.Key, Fund("D", 350m), _actor);
        (await noReason.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("důvodem");

        var request = Fund("D", 350m);
        request.Reason = "chyba v závěrce 2022";
        await Sut().UpdateAsync(created.Key, request, _actor);
        _audit.Verify(x => x.LogAsync(OpeningBalancesUseCase.OpeningBalanceEntity, created.Key, AuditActions.Correction,
            It.Is<object>(o => ((OpeningBalance)o).Value == 300m), It.Is<object>(o => ((OpeningBalance)o).Value == 350m), _actor, "chyba v závěrce 2022"), Times.Once);

        var delete = () => Sut().DeleteAsync(created.Key, "x", _actor);
        await delete.Should().ThrowAsync<BusinessRuleException>();

        var createLocked = () => Sut().CreateAsync(Fund("E", 1m), _actor);
        (await createLocked.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("uzavřené");
    }

    [Fact]
    public async Task DeleteBeforeClosingRemovesTheValue_ButKeepsTheReading()
    {
        await StartAsync();
        var created = await Sut().CreateAsync(Meter("mE", 12m), _actor);

        await Sut().DeleteAsync(created.Key, "omyl", _actor);

        _balances.Items.Should().BeEmpty();
        _readings.Items.Should().HaveCount(1);
        _audit.Verify(x => x.LogAsync(OpeningBalancesUseCase.OpeningBalanceEntity, created.Key, AuditActions.Delete, It.IsAny<object>(), null, _actor, "omyl"), Times.Once);
        var missing = () => Sut().DeleteAsync("nope", null, _actor);
        await missing.Should().ThrowAsync<NotFoundException>();
    }
}
