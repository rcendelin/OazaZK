using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.UseCases;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.BankImport;

public class ImportBankStatementUseCaseTests
{
    private const string OwnKey = "2601634649_2010";

    private readonly Mock<IHouseRepository> _houseRepo = new();
    private readonly Mock<IAdvancePaymentRepository> _advanceRepo = new();
    private readonly Mock<IBankAccountMappingRepository> _mappingRepo = new();
    private readonly Mock<IBankTransactionRepository> _txRepo = new();
    private readonly Mock<IAdvanceSettingsRepository> _settingsRepo = new();
    private readonly List<BankAccountMapping> _mappings = new();
    private readonly List<BankTransaction> _processed = new();
    private readonly Dictionary<string, List<AdvancePayment>> _payments = new();
    private readonly ImportBankStatementUseCase _sut;

    public ImportBankStatementUseCaseTests()
    {
        var houses = Enumerable.Range(1, 8)
            .Select(i => new House { Id = $"house-{i}", Name = $"RD{i}", IsActive = true })
            .ToList();
        _houseRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.House)).ReturnsAsync(houses);

        // Every house prescribes 1500 Kč = 1000 water + 200 electricity + 300 common.
        _settingsRepo.Setup(r => r.GetAsync()).ReturnsAsync(new AdvanceSettings
        {
            HouseOverrides = houses.ToDictionary(h => h.Id, _ => new HouseAdvanceOverride
            {
                WaterAdvance = 1000m, ElectricityAdvance = 200m, CommonAdvance = 300m,
            }),
        });

        _mappingRepo.Setup(r => r.GetAllMappingsAsync()).ReturnsAsync(() => _mappings.ToList());
        _txRepo.Setup(r => r.GetByAccountAsync(OwnKey)).ReturnsAsync(() => _processed.ToList());
        _advanceRepo.Setup(r => r.GetByHouseIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string houseId) => _payments.GetValueOrDefault(houseId)?.ToList() ?? new List<AdvancePayment>());

        var prescribed = TestSupport.PrescribedAdvances.WithoutCosts(_settingsRepo.Object, _houseRepo.Object);
        _sut = new ImportBankStatementUseCase(
            _houseRepo.Object, _advanceRepo.Object, _mappingRepo.Object, _txRepo.Object,
            prescribed, NullLogger<ImportBankStatementUseCase>.Instance);
    }

    private static byte[] Fixture() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "BankImport", "Fixtures", "fio-vypis-anonym.csv"));

    private void Map(string accountKey, string houseId) =>
        _mappings.Add(new BankAccountMapping { AccountKey = accountKey, HouseId = houseId });

    // ───────────────────── Preview ─────────────────────

    [Fact]
    public async Task Preview_AssignsHouseByCounterAccountOnly()
    {
        Map("111111111_0300", "house-2");
        Map("107-2222222222_0100", "house-7");

        var preview = await _sut.PreviewAsync(Fixture());

        preview.Statement.Account.Should().Be("2601634649/2010");
        preview.Statement.SumCheckOk.Should().BeTrue();
        preview.Rows.Should().HaveCount(9);

        var mapped = preview.Rows.Single(r => r.TransactionId == "10000000001");
        mapped.HouseId.Should().Be("house-2");
        mapped.MatchSource.Should().Be(BankImportMatchSource.Account);

        var prefixed = preview.Rows.Single(r => r.TransactionId == "10000000002");
        prefixed.CounterAccount.Should().Be("107-2222222222/0100");
        prefixed.HouseId.Should().Be("house-7", "VS 7 is ignored — only the account decides");

        var unmapped = preview.Rows.Single(r => r.TransactionId == "10000000003");
        unmapped.HouseId.Should().BeNull("the message says RD4, but the account is unknown");
        unmapped.MatchSource.Should().Be(BankImportMatchSource.None);
        unmapped.Status.Should().Be(BankImportRowStatus.New);
    }

    [Fact]
    public async Task Preview_PrescribedAmount_IsAdvanceForPaymentMonthWithPrescribedSplit()
    {
        Map("111111111_0300", "house-2");

        var row = (await _sut.PreviewAsync(Fixture())).Rows.Single(r => r.TransactionId == "10000000001");

        row.PaymentType.Should().Be("Advance");
        (row.Year, row.Month).Should().Be((2026, 8));
        (row.WaterAmount, row.ElectricityAmount, row.CommonAmount).Should().Be((1000m, 200m, 300m));
        row.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_DoplatekInMessage_IsDoplatekWithProportionalSplit()
    {
        Map("8888888888_5500", "house-5");

        var rows = (await _sut.PreviewAsync(Fixture())).Rows;

        var doplatek = rows.Single(r => r.TransactionId == "10000000008");
        doplatek.PaymentType.Should().Be("Doplatek");
        (doplatek.WaterAmount, doplatek.ElectricityAmount, doplatek.CommonAmount).Should().Be((333m, 67m, 100m));

        // The same payer's regular 1500 on the same day is still the month's advance.
        rows.Single(r => r.TransactionId == "10000000009").PaymentType.Should().Be("Advance");
    }

    [Fact]
    public async Task Preview_MonthAlreadyHasAdvance_SuggestsDoplatekWithWarning()
    {
        Map("111111111_0300", "house-2");
        _payments["house-2"] = new List<AdvancePayment>
        {
            new() { HouseId = "house-2", Year = 2026, Month = 8, Type = PaymentType.Advance, Amount = 1500m },
        };

        var preview = await _sut.PreviewAsync(Fixture());
        var row = preview.Rows.Single(r => r.TransactionId == "10000000001");

        row.PaymentType.Should().Be("Doplatek");
        row.Warnings.Should().ContainSingle().Which.Should().Contain("08/2026 už existuje");
        preview.ExistingAdvanceMonths["house-2"].Should().Equal("2026-08");
    }

    [Fact]
    public async Task Preview_SecondPrescribedPaymentInSameMonth_IsDoplatek()
    {
        // Both 1500 payments of house-5 map to it; the doplatek row is excluded by keyword,
        // so give house-5 a second regular-looking payment via another account.
        Map("8888888888_5500", "house-5");
        Map("7777777777_5500", "house-5");

        var rows = (await _sut.PreviewAsync(Fixture())).Rows;

        rows.Single(r => r.TransactionId == "10000000007").PaymentType.Should().Be("Advance");
        rows.Single(r => r.TransactionId == "10000000009").PaymentType.Should().Be("Doplatek");
    }

    [Fact]
    public async Task Preview_UnusualAmount_IsDoplatekWithWarning()
    {
        Map("111111111_0300", "house-2");
        _settingsRepo.Setup(r => r.GetAsync()).ReturnsAsync(new AdvanceSettings
        {
            HouseOverrides = new() { ["house-2"] = new HouseAdvanceOverride { WaterAdvance = 1200m, ElectricityAdvance = 200m, CommonAdvance = 300m } },
        });

        var row = (await _sut.PreviewAsync(Fixture())).Rows.Single(r => r.TransactionId == "10000000001");

        row.PaymentType.Should().Be("Doplatek");
        row.Warnings.Should().ContainSingle().Which.Should().Contain("Neobvyklá částka");
    }

    [Fact]
    public async Task Preview_ProcessedAndOutgoingMovements_AreNotNew()
    {
        _processed.Add(new BankTransaction
        {
            OwnAccountKey = OwnKey, TransactionId = "10000000001", Status = BankTransactionStatus.Imported, HouseId = "house-2",
        });
        _processed.Add(new BankTransaction
        {
            OwnAccountKey = OwnKey, TransactionId = "10000000002", Status = BankTransactionStatus.Ignored,
        });
        var csv = Encoding.UTF8.GetString(Fixture()).Replace("\"10000000003\";\"12.08.2026\";\"1500\"", "\"10000000003\";\"12.08.2026\";\"-1500\"");

        var rows = (await _sut.PreviewAsync(Encoding.UTF8.GetBytes(csv))).Rows;

        rows.Single(r => r.TransactionId == "10000000001").Status.Should().Be(BankImportRowStatus.AlreadyImported);
        rows.Single(r => r.TransactionId == "10000000001").HouseId.Should().Be("house-2");
        rows.Single(r => r.TransactionId == "10000000002").Status.Should().Be(BankImportRowStatus.Ignored);
        rows.Single(r => r.TransactionId == "10000000003").Status.Should().Be(BankImportRowStatus.Outgoing);
    }

    [Theory]
    [InlineData(1500, 1000, 200, 300)]   // prescribed → prescribed split
    [InlineData(500, 333, 67, 100)]      // proportional, water takes the rounding remainder
    [InlineData(3000.50, 2000.50, 400, 600)]
    public void SplitAmount_SumsToAmount(decimal amount, decimal water, decimal electricity, decimal common)
    {
        var split = ImportBankStatementUseCase.SplitAmount(amount, new AdvanceSplit(1000m, 200m, 300m));

        split.Should().Be(new AdvanceSplit(water, electricity, common));
        split.Total.Should().Be(amount);
    }

    [Fact]
    public void SplitAmount_WithoutPrescription_PutsEverythingIntoWater()
    {
        ImportBankStatementUseCase.SplitAmount(700m, null).Should().Be(new AdvanceSplit(700m, 0m, 0m));
        ImportBankStatementUseCase.SplitAmount(700m, new AdvanceSplit(0, 0, 0)).Should().Be(new AdvanceSplit(700m, 0m, 0m));
    }

    // ───────────────────── Confirm ─────────────────────

    private static ConfirmBankImportRow ImportRow(
        string id, string houseId, string type = "Advance", decimal amount = 1500m,
        decimal water = 1000m, decimal electricity = 200m, decimal common = 300m,
        string counterAccount = "3333333333/5500") => new()
    {
        TransactionId = id,
        Date = new DateTime(2026, 8, 12),
        Amount = amount,
        CounterAccount = counterAccount,
        CounterName = "Petr Vzorový",
        Message = "RD4",
        Action = BankImportAction.Import,
        HouseId = houseId,
        PaymentType = type,
        Year = 2026,
        Month = 8,
        WaterAmount = water,
        ElectricityAmount = electricity,
        CommonAmount = common,
    };

    private static ConfirmBankImportRequest Request(params ConfirmBankImportRow[] rows) =>
        new() { Account = "2601634649/2010", Rows = rows.ToList() };

    [Fact]
    public async Task Confirm_ImportsAdvance_RecordsMovement_AndLearnsAccount()
    {
        var result = await _sut.ConfirmAsync(Request(ImportRow("t1", "house-4")), "admin-1");

        result.Imported.Should().Be(1);
        result.NewAccounts.Should().Be(1);
        _advanceRepo.Verify(r => r.UpsertAsync(It.Is<AdvancePayment>(p =>
            p.HouseId == "house-4" && p.Type == PaymentType.Advance && p.RowKey == "2026-08" &&
            p.Amount == 1500m && p.WaterAmount == 1000m && p.Note == "RD4" &&
            p.BankOwnAccountKey == OwnKey && p.BankTransactionId == "t1")), Times.Once);
        _txRepo.Verify(r => r.UpsertAsync(It.Is<BankTransaction>(t =>
            t.OwnAccountKey == OwnKey && t.TransactionId == "t1" && t.Status == BankTransactionStatus.Imported &&
            t.HouseId == "house-4" && t.PaymentRowKey == "2026-08" && t.ImportedBy == "admin-1")), Times.Once);
        _mappingRepo.Verify(r => r.UpsertAsync(It.Is<BankAccountMapping>(m =>
            m.AccountKey == "3333333333_5500" && m.HouseId == "house-4")), Times.Once);
    }

    [Fact]
    public async Task Confirm_AdvanceForAMonthClosedByAnInterimClosing_IsBookedAfterTheCut()
    {
        var prescribed = TestSupport.PrescribedAdvances.WithoutCosts(_settingsRepo.Object, _houseRepo.Object);
        var closed = new ImportBankStatementUseCase(
            _houseRepo.Object, _advanceRepo.Object, _mappingRepo.Object, _txRepo.Object,
            prescribed, NullLogger<ImportBankStatementUseCase>.Instance, new TestSupport.ClosedUntil(new DateOnly(2026, 8, 31)));

        await closed.ConfirmAsync(Request(ImportRow("t1", "house-4")), "admin-1");

        _advanceRepo.Verify(r => r.UpsertAsync(It.Is<AdvancePayment>(p =>
            p.Type == PaymentType.Doplatek && p.RowKey.StartsWith("D-") &&
            p.PaymentDate == new DateTime(2026, 9, 1) && p.Year == 2026 && p.Month == 9 && p.Amount == 1500m &&
            p.Note == "Záloha za 8/2026 zapsaná po mezizávěrce k 31. 8. 2026 — RD4")), Times.Once);
        _txRepo.Verify(r => r.UpsertAsync(It.Is<BankTransaction>(t => t.PaymentRowKey!.StartsWith("D-"))), Times.Once);
    }

    [Fact]
    public async Task Confirm_Doplatek_GetsUniqueRowKeyAndPaymentDate()
    {
        await _sut.ConfirmAsync(Request(ImportRow("t1", "house-4", type: "Doplatek", amount: 500m, water: 333m, electricity: 67m, common: 100m)), "admin-1");

        _advanceRepo.Verify(r => r.UpsertAsync(It.Is<AdvancePayment>(p =>
            p.Type == PaymentType.Doplatek && p.RowKey.StartsWith("D-") &&
            p.PaymentDate == new DateTime(2026, 8, 12) && p.Amount == 500m)), Times.Once);
    }

    [Fact]
    public async Task Confirm_KnownAccountOfSameHouse_IsNotCountedAsNew()
    {
        _mappings.Add(new BankAccountMapping { AccountKey = "3333333333_5500", HouseId = "house-4", AccountName = "Petr Vzorový" });

        var result = await _sut.ConfirmAsync(Request(ImportRow("t1", "house-4")), "admin-1");

        result.NewAccounts.Should().Be(0);
        _mappingRepo.Verify(r => r.UpsertAsync(It.IsAny<BankAccountMapping>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_ReassignedAccount_OverwritesMapping()
    {
        _mappings.Add(new BankAccountMapping { AccountKey = "3333333333_5500", HouseId = "house-1" });

        var result = await _sut.ConfirmAsync(Request(ImportRow("t1", "house-4")), "admin-1");

        result.NewAccounts.Should().Be(1);
        _mappingRepo.Verify(r => r.UpsertAsync(It.Is<BankAccountMapping>(m =>
            m.AccountKey == "3333333333_5500" && m.HouseId == "house-4")), Times.Once);
    }

    [Fact]
    public async Task Confirm_AlreadyProcessedMovement_IsSkipped()
    {
        _processed.Add(new BankTransaction { OwnAccountKey = OwnKey, TransactionId = "t1" });

        var result = await _sut.ConfirmAsync(Request(ImportRow("t1", "house-4")), "admin-1");

        result.Imported.Should().Be(0);
        result.Skipped.Should().Equal("t1");
        _advanceRepo.Verify(r => r.UpsertAsync(It.IsAny<AdvancePayment>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_Ignore_RecordsMovementOnly()
    {
        var row = ImportRow("t1", "house-4");
        row.Action = BankImportAction.Ignore;
        row.HouseId = null;

        var result = await _sut.ConfirmAsync(Request(row), "admin-1");

        result.Ignored.Should().Be(1);
        _txRepo.Verify(r => r.UpsertAsync(It.Is<BankTransaction>(t => t.Status == BankTransactionStatus.Ignored)), Times.Once);
        _advanceRepo.Verify(r => r.UpsertAsync(It.IsAny<AdvancePayment>()), Times.Never);
        _mappingRepo.Verify(r => r.UpsertAsync(It.IsAny<BankAccountMapping>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_AdvanceCollidingWithStoredAdvance_FailsWithoutWriting()
    {
        _payments["house-4"] = new List<AdvancePayment>
        {
            new() { HouseId = "house-4", Year = 2026, Month = 8, Type = PaymentType.Advance },
        };

        var act = () => _sut.ConfirmAsync(Request(ImportRow("t0", "house-1"), ImportRow("t1", "house-4")), "admin-1");

        await act.Should().ThrowAsync<AppException>().WithMessage("*t1*08/2026*");
        _advanceRepo.Verify(r => r.UpsertAsync(It.IsAny<AdvancePayment>()), Times.Never);
        _txRepo.Verify(r => r.UpsertAsync(It.IsAny<BankTransaction>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_TwoAdvancesForSameHouseMonth_Fail()
    {
        var act = () => _sut.ConfirmAsync(Request(ImportRow("t1", "house-4"), ImportRow("t2", "house-4")), "admin-1");

        await act.Should().ThrowAsync<AppException>().WithMessage("*t2*");
    }

    [Theory]
    [InlineData(1000, 200, 200)]  // sum 1400 ≠ 1500
    [InlineData(1600, -100, 0)]   // negative component
    public async Task Confirm_InvalidSplit_Fails(decimal water, decimal electricity, decimal common)
    {
        var act = () => _sut.ConfirmAsync(Request(ImportRow("t1", "house-4", water: water, electricity: electricity, common: common)), "admin-1");

        await act.Should().ThrowAsync<AppException>();
        _advanceRepo.Verify(r => r.UpsertAsync(It.IsAny<AdvancePayment>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_UnknownHouse_Fails()
    {
        var act = () => _sut.ConfirmAsync(Request(ImportRow("t1", "no-such-house")), "admin-1");

        await act.Should().ThrowAsync<AppException>().WithMessage("*domácnost*");
    }
}
