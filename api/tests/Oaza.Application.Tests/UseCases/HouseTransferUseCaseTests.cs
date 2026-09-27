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

/// <summary>S4: house B sold on 15. 3. — closing of B at 14. 3., the old owner's closing saldo, the new owner starts fresh.</summary>
public class HouseTransferUseCaseTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private static readonly DateOnly Start = D(2023, 11, 1);
    private static readonly DateOnly Sale = D(2026, 3, 15);

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
    private readonly PragueClock _clock = new(new FixedTimeProvider(new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero)));

    public HouseTransferUseCaseTests()
    {
        foreach (var name in new[] { "A", "B", "C", "D" })
        {
            _houses.UpsertAsync(new House { Id = name, Name = $"RD {name}", ContactPerson = $"Původní {name}", Email = $"{name}@example.cz", IsActive = true });
            _periods.UpsertAsync(new OwnershipPeriod { Id = OwnershipPeriod.KeyFor(name, Start), HouseId = name, OwnerName = $"Původní {name}", ValidFrom = Start });
            _participations.UpsertAsync(new Participation { Id = name, ComponentId = "vodarna", HouseId = name, ValidFrom = Start });
        }
        _meters.UpsertAsync(new WaterMeter { Id = "mB", MeterNumber = "V-B", Type = MeterType.Individual, HouseId = "B" });
        _readings.UpsertAsync(new MeterReading { MeterId = "mB", ReadingDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Value = 1_230m });
        _readings.UpsertAsync(new MeterReading { MeterId = "mB", ReadingDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), Value = 1_240m });
        _components.UpsertAsync(new CostComponent { Id = "vodarna", Name = "Elektřina – vodárna", Code = "ELEKTRINA_VODARNA", StartDate = Start, AllocationBasis = AllocationBasis.CostEntries });
        _rules.UpsertAsync(new ComponentAllocationRule { Id = "r", ComponentId = "vodarna", ValidFrom = Start, Method = AllocationMethod.Equal });
        _entries.UpsertAsync(new CostEntry { Id = "mar", ComponentId = "vodarna", Type = CostEntryType.Advance, PeriodFrom = D(2026, 3, 1), PeriodTo = D(2026, 3, 31), Amount = 3_100m });
        _payments.UpsertAsync(new AdvancePayment { HouseId = "B", RowKey = "2026-03", Type = PaymentType.Advance, Year = 2026, Month = 3, Amount = 1_000m });
    }

    private HouseLedgerUseCase Ledger() => new(
        new LedgerCostCollector(_components, _rules, _participations, _entries, _openings, _meters, _readings, _houses),
        _houses, _payments, _openings, _periods, _components, _clock);

    private HouseTransferUseCase Sut()
    {
        var boundary = new InterimClosingBoundary(_closings);
        var closings = new InterimClosingsUseCase(_closings, _houses, Ledger(), _audit.Object, _clock);
        var openings = new OpeningBalancesUseCase(_openings, _periods, _houses, _meters, _readings, _components, _rules, _participations, boundary, _audit.Object, _clock);
        return new HouseTransferUseCase(_houses, _periods, _meters, _readings, boundary, Ledger(), closings, openings, _audit.Object, _clock);
    }

    private static HouseTransferRequest Request(decimal? meter = 1_234.567m) => new()
    {
        TransferDate = Sale, NewOwnerName = "Noví vlastníci", NewOwnerContact = "novi@example.cz", MeterValue = meter, MeterSource = "předávací protokol",
    };

    [Fact]
    public async Task PreviewShowsTheOldOwnersClosingSaldoAndAMeterSuggestion()
    {
        var preview = await Sut().PreviewAsync("B", Sale);

        preview.ClosingDate.Should().Be(D(2026, 3, 14));
        preview.CurrentOwnerName.Should().Be("Původní B");
        preview.ClosingCosts.Should().Be(350m);        // 14 of 31 days of 3 100 Kč, a quarter
        preview.ClosingPayments.Should().Be(1_000m);
        preview.ClosingSaldo.Should().Be(650m);         // přeplatek of the old owner
        preview.MeterNumber.Should().Be("V-B");
        preview.SuggestedMeterValue.Should().Be(1_234.516m); // 1 230 + 10 × 14/31
        preview.Problems.Should().BeEmpty();
    }

    [Fact]
    public async Task S4_TransferClosesTheOldOwnerAndStartsTheNewOneFresh()
    {
        var result = await Sut().TransferAsync("B", Request(), _actor);

        // Closing of B at 14. 3. with the old owner's closing saldo.
        var closing = _closings.Items.Values.Single();
        closing.Id.Should().Be(result.ClosingId).And.Be("2026-03-14|House|B");
        result.ClosingSaldo.Should().Be(650m);

        // Old period ends, new one starts on the sale day.
        var periods = _periods.Items.Values.Where(p => p.HouseId == "B").OrderBy(p => p.ValidFrom).ToList();
        periods.Select(p => (p.OwnerName, p.ValidFrom, p.ValidTo)).Should().Equal(
            ("Původní B", Start, (DateOnly?)D(2026, 3, 14)), ("Noví vlastníci", Sale, (DateOnly?)null));
        result.NewOwnershipPeriodId.Should().Be("B|2026-03-15");

        // New owner: fund 0, meter 1 234,567 m³ as the reading on the sale day.
        _openings.Items.Values.Should().ContainSingle(o => o.Type == OpeningBalanceType.FundShare && o.OwnershipPeriodId == "B|2026-03-15" && o.Value == 0m);
        _readings.Items.Values.Should().ContainSingle(r => r.MeterId == "mB" && r.ReadingDate == new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc) && r.Value == 1_234.567m);

        // Costs from 15. 3. go to the new ownership period.
        var ledger = await Ledger().GetHouseLedgerAsync("B", null, null, D(2026, 3, 31), new LedgerRequester(UserRole.Admin, null));
        ledger.OwnershipPeriod!.OwnerName.Should().Be("Noví vlastníci");
        ledger.From.Should().Be(Sale);
        ledger.Opening.Should().Be(0m);
        ledger.Costs.Should().Be(425m);  // 17 of 31 days, a quarter
        ledger.Payments.Should().Be(0m);  // the March advance was paid by the old owner

        // House contact is the new owner now.
        var house = _houses.Items.Values.Single(h => h.Id == "B");
        (house.ContactPerson, house.Email).Should().Be(("Noví vlastníci", "novi@example.cz"));
    }

    [Fact]
    public async Task InvalidTransfersAreRejectedBeforeAnythingIsWritten()
    {
        var future = Request();
        future.TransferDate = D(2026, 3, 21);
        var inFuture = () => Sut().TransferAsync("B", future, _actor);
        (await inFuture.Should().ThrowAsync<BusinessRuleException>()).Which.Errors.Should().Contain(e => e.Contains("nejpozději k dnešku"));

        var noMeter = () => Sut().TransferAsync("B", Request(meter: null), _actor);
        (await noMeter.Should().ThrowAsync<BusinessRuleException>()).Which.Errors.Should().Contain(e => e.Contains("V-B"));

        var noOwner = Request();
        noOwner.NewOwnerName = " ";
        var missingOwner = () => Sut().TransferAsync("B", noOwner, _actor);
        (await missingOwner.Should().ThrowAsync<BusinessRuleException>()).Which.Errors.Should().Contain(e => e.Contains("nového vlastníka"));

        _closings.Items.Should().BeEmpty();
        _periods.Items.Values.Should().OnlyContain(p => p.ValidTo == null);

        await Sut().TransferAsync("B", Request(), _actor);
        var again = () => Sut().TransferAsync("B", Request(), _actor);
        (await again.Should().ThrowAsync<BusinessRuleException>()).Which.Errors.Should().Contain(e => e.Contains("uzavřené mezizávěrkou"));
        var missing = () => Sut().PreviewAsync("Z", Sale);
        await missing.Should().ThrowAsync<NotFoundException>();
    }
}
