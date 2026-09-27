using System.Text;
using ClosedXML.Excel;
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

public class CashBookUseCaseTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private static readonly DateOnly Start = D(2023, 11, 1);

    private readonly MemoryCashBook _book = new();
    private readonly MemoryComponents _components = new();
    private readonly MemoryRules _rules = new();
    private readonly MemoryParticipations _participations = new();
    private readonly MemoryCostEntries _entries = new();
    private readonly MemoryHouses _houses = new();
    private readonly ClosedUntil _closed = new(null);
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly AuditActor _actor = new("acc-1", "Radka");
    private readonly PragueClock _clock = new(new FixedTimeProvider(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero)));

    public CashBookUseCaseTests()
    {
        _components.UpsertAsync(new CostComponent { Id = "okoli", Name = "Údržba okolí", Code = "UDRZBA_OKOLI", StartDate = Start, AllocationBasis = AllocationBasis.CostEntries });
        _rules.UpsertAsync(new ComponentAllocationRule { Id = "r", ComponentId = "okoli", ValidFrom = Start, Method = AllocationMethod.Equal });
        foreach (var house in new[] { "A", "B" })
        {
            _houses.UpsertAsync(new House { Id = house, Name = $"RD {house}" });
            _participations.UpsertAsync(new Participation { Id = house, ComponentId = "okoli", HouseId = house, ValidFrom = Start });
        }
    }

    private CashBookUseCase Sut() => new(
        _book, _components,
        new CostEntriesUseCase(_entries, _components, _rules, _participations, _houses, _closed, _audit.Object),
        _closed, _audit.Object, _clock);

    private static CreateCashBookEntryRequest Deposit(decimal amount, DateOnly date) => new()
    {
        Date = date, Type = CashBookEntryType.Deposit, Amount = amount, Category = "výběr z účtu", Description = "Výběr z účtu", BankTransactionRef = "Fio 123",
    };

    private static CreateCashBookEntryRequest Expense(decimal amount, DateOnly date, bool receipt = false, string? counterparty = "Pan Novák") => new()
    {
        Date = date, Type = CashBookEntryType.Expense, Amount = amount, Category = "údržba okolí", Description = "úprava okolí",
        Counterparty = counterparty, HasReceipt = receipt,
    };

    [Fact]
    public async Task S5_DepositExpenseRejectedExpenseAndStorno()
    {
        await Sut().CreateAsync(Deposit(5_000m, D(2026, 9, 1)), _actor);
        var expense = await Sut().CreateAsync(Expense(2_000m, D(2026, 9, 2)), _actor);
        expense.Balance.Should().Be(3_000m);
        expense.HasReceipt.Should().BeFalse();

        var tooMuch = () => Sut().CreateAsync(Expense(4_000m, D(2026, 9, 3)), _actor);
        (await tooMuch.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("záporný zůstatek");

        var storno = await Sut().StornoAsync(expense.Id, new StornoCashBookEntryRequest { Reason = "zaplaceno z účtu" }, _actor);
        storno.Type.Should().Be(CashBookEntryType.Correction);
        storno.Effect.Should().Be(2_000m);
        storno.Balance.Should().Be(5_000m);
        storno.Date.Should().Be(D(2026, 9, 27));

        var book = await Sut().ListAsync(null, null);
        book.ClosingBalance.Should().Be(5_000m);
        book.Entries.Single(e => e.Id == expense.Id).CorrectedBy.Should().Be(storno.Id);
        _audit.Verify(a => a.LogAsync(CashBookUseCase.CashBookEntity, storno.Id, AuditActions.Correction, It.IsAny<object>(), It.IsAny<object>(), _actor, "zaplaceno z účtu"), Times.Once);
    }

    [Fact]
    public async Task BackdatedExpenseThatWouldMakeALaterBalanceNegativeIsRejected()
    {
        await Sut().CreateAsync(Deposit(1_000m, D(2026, 9, 1)), _actor);
        await Sut().CreateAsync(Expense(800m, D(2026, 9, 10)), _actor);

        var backdated = () => Sut().CreateAsync(Expense(300m, D(2026, 9, 5)), _actor);

        (await backdated.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("-100,00 Kč k 10. 9. 2026");
    }

    [Fact]
    public async Task ExpenseWithoutReceiptNeedsCounterparty_AndInvalidInputIsRejected()
    {
        await Sut().CreateAsync(Deposit(1_000m, D(2026, 9, 1)), _actor);

        var noCounterparty = () => Sut().CreateAsync(Expense(100m, D(2026, 9, 2), receipt: false, counterparty: " "), _actor);
        (await noCounterparty.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("komu");
        (await Sut().CreateAsync(Expense(100m, D(2026, 9, 2), receipt: true, counterparty: null), _actor)).HasReceipt.Should().BeTrue();

        var zero = () => Sut().CreateAsync(Deposit(0m, D(2026, 9, 1)), _actor);
        await zero.Should().ThrowAsync<BusinessRuleException>();
        var future = () => Sut().CreateAsync(Deposit(1m, D(2026, 9, 28)), _actor);
        (await future.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("budoucnosti");
        var correction = () => Sut().CreateAsync(new CreateCashBookEntryRequest { Type = CashBookEntryType.Correction, Date = D(2026, 9, 1), Amount = 1, Description = "x" }, _actor);
        await correction.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task StornoRules()
    {
        var deposit = await Sut().CreateAsync(Deposit(1_000m, D(2026, 9, 1)), _actor);
        await Sut().CreateAsync(Expense(800m, D(2026, 9, 2)), _actor);

        var depositStorno = () => Sut().StornoAsync(deposit.Id, new StornoCashBookEntryRequest { Reason = "omyl" }, _actor);
        (await depositStorno.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("záporný zůstatek");

        var noReason = () => Sut().StornoAsync(deposit.Id, new StornoCashBookEntryRequest { Reason = "" }, _actor);
        await noReason.Should().ThrowAsync<BusinessRuleException>();

        var second = await Sut().CreateAsync(Deposit(50m, D(2026, 9, 3)), _actor);
        var storno = await Sut().StornoAsync(second.Id, new StornoCashBookEntryRequest { Reason = "omyl" }, _actor);
        var twice = () => Sut().StornoAsync(second.Id, new StornoCashBookEntryRequest { Reason = "znovu" }, _actor);
        (await twice.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("už je stornovaný");
        var ofStorno = () => Sut().StornoAsync(storno.Id, new StornoCashBookEntryRequest { Reason = "x" }, _actor);
        await ofStorno.Should().ThrowAsync<BusinessRuleException>();
        var missing = () => Sut().StornoAsync("nope", new StornoCashBookEntryRequest { Reason = "x" }, _actor);
        await missing.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SharedCostPaidFromCashCreatesACostEntry_AndItsStornoCorrectsIt()
    {
        await Sut().CreateAsync(Deposit(5_000m, D(2026, 9, 1)), _actor);
        var request = Expense(1_200m, D(2026, 9, 2));
        request.ComponentId = "okoli";

        var expense = await Sut().CreateAsync(request, _actor);

        expense.ComponentName.Should().Be("Údržba okolí");
        var cost = _entries.Items.Values.Single();
        expense.CostEntryId.Should().Be(cost.Id);
        (cost.Type, cost.Amount, cost.PaidFrom).Should().Be((CostEntryType.OneOff, 1_200m, PaidFrom.Cash));

        await Sut().StornoAsync(expense.Id, new StornoCashBookEntryRequest { Reason = "vráceno" }, _actor);

        var correction = _entries.Items.Values.Single(e => e.Id != cost.Id);
        (correction.Amount, correction.CorrectionOf).Should().Be((-1_200m, cost.Id));
    }

    [Fact]
    public async Task ClosedDaysCannotGetNewEntries()
    {
        _closed.Day = D(2026, 8, 31);

        var closed = () => Sut().CreateAsync(Deposit(100m, D(2026, 8, 31)), _actor);
        (await closed.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("mezizávěrkou k 31. 8. 2026");
        (await Sut().CreateAsync(Deposit(100m, D(2026, 9, 1)), _actor)).Balance.Should().Be(100m);
    }

    [Fact]
    public async Task ListByPeriodAndExportsIncludeEverythingWithTheNoReceiptFlag()
    {
        await Sut().CreateAsync(Deposit(5_000m, D(2026, 8, 1)), _actor);
        await Sut().CreateAsync(Expense(2_000m, D(2026, 9, 2)), _actor);
        await Sut().CreateAsync(Expense(500m, D(2026, 9, 3), receipt: true), _actor);

        var september = await Sut().ListAsync(D(2026, 9, 1), D(2026, 9, 30));
        (september.OpeningBalance, september.Deposits, september.Expenses, september.ClosingBalance).Should().Be((5_000m, 0m, 2_500m, 2_500m));
        september.Entries.Should().HaveCount(2);
        var reversed = () => Sut().ListAsync(D(2026, 9, 30), D(2026, 9, 1));
        await reversed.Should().ThrowAsync<AppException>();

        var xlsx = await Sut().ExportAsync(D(2026, 9, 1), D(2026, 9, 30), "xlsx");
        xlsx.FileName.Should().Be("pokladni-kniha-20260901-20260930.xlsx");
        using (var workbook = new XLWorkbook(new MemoryStream(xlsx.Content)))
        {
            var ws = workbook.Worksheet("Pokladní kniha");
            ws.Cell(5, 6).GetString().Should().Be("bez dokladu");
            ws.Cell(6, 6).GetString().Should().Be("ano");
            ws.Cell(5, 8).GetValue<decimal>().Should().Be(2_000m);
        }

        var pdf = await Sut().ExportAsync(null, null, "pdf");
        pdf.ContentType.Should().Be("application/pdf");
        Encoding.ASCII.GetString(pdf.Content, 0, 5).Should().Be("%PDF-");
        var bad = () => Sut().ExportAsync(null, null, "doc");
        await bad.Should().ThrowAsync<AppException>();
    }
}
