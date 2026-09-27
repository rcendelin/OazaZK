using System.Reflection;
using FluentAssertions;
using Moq;
using Oaza.Application.Audit;
using Oaza.Application.Deployment;
using Oaza.Application.Exceptions;
using Oaza.Application.OffBookFunds;
using Oaza.Application.Tests.TestSupport;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Domain.Time;

namespace Oaza.Application.Tests.OffBookFunds;

public class OffBookFundUseCaseTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private static readonly string[] SixHouses = ["A", "B", "C", "D", "E", "F"];

    private readonly MemoryOffBookFunds _repository = new();
    private readonly MemoryHouses _houses = new();
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly AuditActor _actor = new("admin-1", "Rosťa");
    private readonly PragueClock _clock = new(new FixedTimeProvider(new DateTimeOffset(2026, 12, 20, 10, 0, 0, TimeSpan.Zero)));

    public OffBookFundUseCaseTests()
    {
        foreach (var h in SixHouses)
            _houses.UpsertAsync(new House { Id = h, Name = $"RD {h}" });
    }

    private OffBookFundUseCase Sut(bool enabled = true) => new(_repository, _houses, new FeatureFlags(enabled), _audit.Object, _clock);

    private async Task<string> FireworksAsync() =>
        (await Sut().CreateAsync(new SaveOffBookFundRequest { Name = "Fond na ohňostroje", ManagerName = "Jindra", AccountDescription = "soukromý účet správce" }, _actor)).Id;

    [Fact]
    public async Task S6_CallForSixHouses_FivePay_OneDebtor_Balance6150()
    {
        var fund = await FireworksAsync();
        var call = await Sut().AddRecordAsync(fund, new AddFundRecordRequest
        {
            Kind = FundRecordKind.Call, Date = D(2026, 12, 1), DueDate = D(2026, 12, 15), Amount = 1_230m, Text = "Silvestr 2026", HouseIds = [.. SixHouses],
        }, _actor);
        foreach (var house in SixHouses.Take(5))
            await Sut().AddRecordAsync(fund, new AddFundRecordRequest { Kind = FundRecordKind.Contribution, Date = D(2026, 12, 10), Amount = 1_230m, HouseId = house, CallId = call.Id }, _actor);

        var detail = await Sut().GetAsync(fund);

        detail.Fund.Balance.Should().Be(6_150m);
        var status = detail.Calls.Single();
        status.Debtors.Should().Be(1);
        status.Collected.Should().Be(6_150m);
        status.Houses.Single(h => !h.IsPaid).HouseName.Should().Be("RD F");
        detail.Records.Should().HaveCount(5);
    }

    [Fact]
    public async Task ExpensePaidInAdvanceIsOwedUntilSettled()
    {
        var fund = await FireworksAsync();
        await Sut().AddRecordAsync(fund, new AddFundRecordRequest { Kind = FundRecordKind.Contribution, Date = D(2026, 12, 1), Amount = 2_000m, HouseId = "A" }, _actor);
        var tooMuch = () => Sut().AddRecordAsync(fund, new AddFundRecordRequest { Kind = FundRecordKind.Expense, Date = D(2026, 12, 2), Amount = 3_000m, Text = "ohňostroj" }, _actor);
        (await tooMuch.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("předem");

        var expense = await Sut().AddRecordAsync(fund, new AddFundRecordRequest { Kind = FundRecordKind.Expense, Date = D(2026, 12, 2), Amount = 3_000m, Text = "ohňostroj", PaidBy = "Jindra" }, _actor);
        (await Sut().ListAsync()).Single().Outstanding.Should().Be(3_000m);

        await Sut().AddRecordAsync(fund, new AddFundRecordRequest { Kind = FundRecordKind.Settlement, Date = D(2026, 12, 3), Amount = 2_000m, ExpenseId = expense.Id }, _actor);
        var after = (await Sut().ListAsync()).Single();
        (after.Balance, after.Outstanding).Should().Be((0m, 1_000m));

        var overSettle = () => Sut().AddRecordAsync(fund, new AddFundRecordRequest { Kind = FundRecordKind.Settlement, Date = D(2026, 12, 3), Amount = 1_001m, ExpenseId = expense.Id }, _actor);
        await overSettle.Should().ThrowAsync<BusinessRuleException>();
        var withSettlement = () => Sut().DeleteRecordAsync(fund, expense.Id, "omyl", _actor);
        await withSettlement.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task InvalidInputIsRejected()
    {
        var accountNumber = () => Sut().CreateAsync(new SaveOffBookFundRequest { Name = "F", ManagerName = "J", AccountDescription = "účet 123456789/0800" }, _actor);
        (await accountNumber.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("číslo účtu");
        var missing = () => Sut().CreateAsync(new SaveOffBookFundRequest(), _actor);
        (await missing.Should().ThrowAsync<BusinessRuleException>()).Which.Errors.Should().HaveCount(2);

        var fund = await FireworksAsync();
        var call = await Sut().AddRecordAsync(fund, new AddFundRecordRequest { Kind = FundRecordKind.Call, Date = D(2026, 12, 1), Amount = 100m, HouseIds = ["A"] }, _actor);
        var notInCall = () => Sut().AddRecordAsync(fund, new AddFundRecordRequest { Kind = FundRecordKind.Contribution, Date = D(2026, 12, 2), Amount = 100m, HouseId = "B", CallId = call.Id }, _actor);
        (await notInCall.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("není ve vybrané výzvě");
        var future = () => Sut().AddRecordAsync(fund, new AddFundRecordRequest { Kind = FundRecordKind.Contribution, Date = D(2027, 1, 1), Amount = 100m, HouseId = "A" }, _actor);
        await future.Should().ThrowAsync<BusinessRuleException>();
        var noHouses = () => Sut().AddRecordAsync(fund, new AddFundRecordRequest { Kind = FundRecordKind.Call, Date = D(2026, 12, 1), Amount = 100m }, _actor);
        await noHouses.Should().ThrowAsync<BusinessRuleException>();

        await Sut().AddRecordAsync(fund, new AddFundRecordRequest { Kind = FundRecordKind.Contribution, Date = D(2026, 12, 2), Amount = 100m, HouseId = "A", CallId = call.Id }, _actor);
        var callWithContributions = () => Sut().DeleteRecordAsync(fund, call.Id, "omyl", _actor);
        await callWithContributions.Should().ThrowAsync<BusinessRuleException>();

        var updated = await Sut().UpdateAsync(fund, new SaveOffBookFundRequest { Name = "Fond na ohňostroje", ManagerName = "Radka", Active = false }, _actor);
        (updated.ManagerName, updated.Active, updated.Balance).Should().Be(("Radka", false, 100m));
    }

    [Fact]
    public async Task WithTheFlagOffEverythingIsNotFound()
    {
        var off = Sut(enabled: false);

        foreach (var act in new Func<Task>[]
        {
            () => off.ListAsync(),
            () => off.GetAsync("x"),
            () => off.CreateAsync(new SaveOffBookFundRequest { Name = "F", ManagerName = "J" }, _actor),
            () => off.UpdateAsync("x", new SaveOffBookFundRequest(), _actor),
            () => off.AddRecordAsync("x", new AddFundRecordRequest(), _actor),
            () => off.DeleteRecordAsync("x", "y", "z", _actor),
        })
        {
            (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(404);
        }
        _repository.Funds.Should().BeEmpty();
        FeatureFlags.IsOn("True").Should().BeTrue();
        FeatureFlags.IsOn(null).Should().BeFalse();
        FeatureFlags.IsOn("1").Should().BeFalse();
    }

    /// <summary>
    /// S6 isolation: nothing but the off-book fund module may touch its data — so fund money can never reach the house
    /// saldo, interim closings (annual closing export), the cash book or the association's finance.
    /// </summary>
    [Fact]
    public void OnlyTheFundModuleDependsOnTheFundRepository()
    {
        var assemblies = new[] { typeof(OffBookFundUseCase).Assembly, typeof(IOffBookFundRepository).Assembly };
        var users = assemblies.SelectMany(a => a.GetTypes())
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p =>
                p.ParameterType == typeof(IOffBookFundRepository) || p.ParameterType == typeof(OffBookFundUseCase))))
            .Select(t => t.FullName)
            .ToList();

        users.Should().Equal(typeof(OffBookFundUseCase).FullName);
        typeof(Oaza.Application.Ledger.HouseLedgerUseCase).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType.Namespace)
            .Should().NotContain("Oaza.Application.OffBookFunds");
    }
}
