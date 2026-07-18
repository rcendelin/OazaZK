using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Oaza.Application.Exceptions;
using Oaza.Application.UseCases;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.UseCases;

public class CloseBillingPeriodUseCaseTests
{
    private readonly Mock<IBillingPeriodRepository> _billingRepo = new();
    private readonly Mock<IHouseRepository> _houseRepo = new();
    private readonly Mock<IWaterMeterRepository> _meterRepo = new();
    private readonly Mock<IMeterReadingRepository> _readingRepo = new();
    private readonly Mock<ISupplierInvoiceRepository> _invoiceRepo = new();
    private readonly Mock<IAdvancePaymentRepository> _advanceRepo = new();
    private readonly Mock<IAdvanceSettingsRepository> _settingsRepo = new();
    private readonly Mock<ISettlementRepository> _settlementRepo = new();
    private readonly Mock<IFinancialRecordRepository> _financialRecordRepo = new();

    private readonly CloseBillingPeriodUseCase _sut;
    private readonly Dictionary<string, List<AdvancePayment>> _paymentsByHouse = new();

    private static readonly DateTime Start = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc);

    public CloseBillingPeriodUseCaseTests()
    {
        _settingsRepo.Setup(r => r.GetAsync()).ReturnsAsync(new AdvanceSettings());
        _financialRecordRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<FinancialRecord>());

        var calc = new CalculateSettlementUseCase(
            _billingRepo.Object, _houseRepo.Object, _meterRepo.Object,
            _readingRepo.Object, _invoiceRepo.Object, _advanceRepo.Object,
            _settingsRepo.Object,
            Mock.Of<ILogger<CalculateSettlementUseCase>>());

        var fundBalanceUseCase = new GetFundBalanceUseCase(
            _houseRepo.Object, _advanceRepo.Object, _financialRecordRepo.Object);

        _sut = new CloseBillingPeriodUseCase(
            calc, _billingRepo.Object, _settlementRepo.Object,
            _houseRepo.Object, _advanceRepo.Object, _financialRecordRepo.Object, _settingsRepo.Object,
            fundBalanceUseCase,
            Mock.Of<ILogger<CloseBillingPeriodUseCase>>());
    }

    private BillingPeriod SetupScenario(BillingPeriodStatus status = BillingPeriodStatus.Open)
    {
        var period = new BillingPeriod
        {
            Id = "period-1",
            Name = "H1 2025",
            DateFrom = Start,
            DateTo = End,
            Status = status,
        };
        _billingRepo.Setup(r => r.GetAsync(PartitionKeys.Period, "period-1")).ReturnsAsync(period);

        _houseRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.House)).ReturnsAsync(new List<House>
        {
            new() { Id = "house-1", Name = "A", IsActive = true },
            new() { Id = "house-2", Name = "B", IsActive = true },
        });
        _meterRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Meter)).ReturnsAsync(new List<WaterMeter>
        {
            new() { Id = "main", Type = MeterType.Main },
            new() { Id = "m1", Type = MeterType.Individual, HouseId = "house-1" },
            new() { Id = "m2", Type = MeterType.Individual, HouseId = "house-2" },
        });
        Readings("main", (Start, 100m), (End, 200m)); // 100
        Readings("m1", (Start, 50m), (End, 80m));      // 30
        Readings("m2", (Start, 20m), (End, 80m));      // 60  -> loss 10, equal 5 each
        _invoiceRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Invoice)).ReturnsAsync(new List<SupplierInvoice>
        {
            new() { Id = "inv", Year = 2025, Month = 3, Amount = 10000m },
        });
        Advances("house-1", 3000m);
        Advances("house-2", 5000m);
        return period;
    }

    private void Readings(string meterId, params (DateTime date, decimal value)[] readings) =>
        _readingRepo.Setup(r => r.GetByMeterIdAsync(meterId)).ReturnsAsync(
            readings.Select(x => new MeterReading
            {
                MeterId = meterId,
                ReadingDate = x.date,
                Value = x.value,
                Source = ReadingSource.Manual,
                ImportedAt = DateTime.UtcNow,
                ImportedBy = "test",
            }).ToList());

    private void Advances(string houseId, decimal waterAmount, decimal commonAmount = 0m)
    {
        var list = new List<AdvancePayment>
        {
            new() { HouseId = houseId, Year = 2025, Month = 3, Amount = waterAmount + commonAmount, WaterAmount = waterAmount, CommonAmount = commonAmount },
        };
        _paymentsByHouse[houseId] = list;
        _advanceRepo.Setup(r => r.GetByHouseAndPeriodAsync(houseId, Start, End)).ReturnsAsync(() => _paymentsByHouse[houseId]);
        _advanceRepo.Setup(r => r.GetByHouseIdAsync(houseId)).ReturnsAsync(() => _paymentsByHouse[houseId]);
        _advanceRepo.Setup(r => r.UpsertAsync(It.Is<AdvancePayment>(p => p.HouseId == houseId)))
            .Callback<AdvancePayment>(p => _paymentsByHouse[houseId].Add(p))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task CloseAsync_PersistsOneSettlementPerHouse_AndLocksPeriod()
    {
        // Arrange
        var period = SetupScenario();
        var persisted = new List<Settlement>();
        _settlementRepo.Setup(r => r.UpsertAsync(It.IsAny<Settlement>()))
            .Callback<Settlement>(persisted.Add)
            .Returns(Task.CompletedTask);
        _billingRepo.Setup(r => r.UpsertAsync(It.IsAny<BillingPeriod>())).Returns(Task.CompletedTask);

        // Act
        var result = await _sut.CloseAsync("period-1", LossAllocationMethod.Equal);

        // Assert: a settlement was persisted for every house with the calculated numbers
        persisted.Should().HaveCount(2);
        result.Should().HaveCount(2);

        var a = persisted.Single(s => s.HouseId == "house-1");
        a.PeriodId.Should().Be("period-1");
        a.ConsumptionM3.Should().Be(30m);
        a.LossAllocatedM3.Should().Be(5m);
        a.SharePercent.Should().Be(35m);
        a.CalculatedAmount.Should().Be(3500m);
        a.TotalAdvances.Should().Be(3000m);
        a.Balance.Should().Be(500m);

        var b = persisted.Single(s => s.HouseId == "house-2");
        b.CalculatedAmount.Should().Be(6500m);
        b.Balance.Should().Be(1500m);

        // Period is flipped to Closed and persisted (irreversible).
        period.Status.Should().Be(BillingPeriodStatus.Closed);
        _billingRepo.Verify(r => r.UpsertAsync(It.Is<BillingPeriod>(p => p.Status == BillingPeriodStatus.Closed)), Times.Once);
    }

    [Fact]
    public async Task CloseAsync_AlreadyClosedPeriod_Throws_AndPersistsNothing()
    {
        // Arrange
        SetupScenario(BillingPeriodStatus.Closed);

        // Act
        var act = () => _sut.CloseAsync("period-1", LossAllocationMethod.Equal);

        // Assert
        await act.Should().ThrowAsync<AppException>();
        _settlementRepo.Verify(r => r.UpsertAsync(It.IsAny<Settlement>()), Times.Never);
        _billingRepo.Verify(r => r.UpsertAsync(It.IsAny<BillingPeriod>()), Times.Never);
    }

    [Fact]
    public async Task CloseAsync_WithFundDraw_CreatesOneDoplatekPerActiveHouse_OneExpense_AndReducesBalance()
    {
        // Arrange: give both houses a common-fund contribution so the fund has 10,000 Kč available.
        var period = SetupScenario();
        Advances("house-1", 3000m, commonAmount: 5000m);
        Advances("house-2", 5000m, commonAmount: 5000m);
        _settlementRepo.Setup(r => r.UpsertAsync(It.IsAny<Settlement>())).Returns(Task.CompletedTask);
        _billingRepo.Setup(r => r.UpsertAsync(It.IsAny<BillingPeriod>())).Returns(Task.CompletedTask);

        // Act: draw 800 Kč, split evenly across the 2 active houses (400 Kč each).
        var result = await _sut.CloseAsync("period-1", LossAllocationMethod.Equal, fundDrawAmount: 800m);

        // Assert: one fund doplatek per active house.
        _advanceRepo.Verify(r => r.UpsertAsync(It.Is<AdvancePayment>(p =>
            p.HouseId == "house-1" && p.WaterAmount == 400m && p.Type == PaymentType.Doplatek && p.IsFundTransfer)), Times.Once);
        _advanceRepo.Verify(r => r.UpsertAsync(It.Is<AdvancePayment>(p =>
            p.HouseId == "house-2" && p.WaterAmount == 400m && p.Type == PaymentType.Doplatek && p.IsFundTransfer)), Times.Once);

        // Assert: one financial-record expense reducing the fund.
        _financialRecordRepo.Verify(r => r.UpsertAsync(It.Is<FinancialRecord>(f =>
            f.Category == "fond-voda" && f.Amount == 800m && f.Type == FinancialRecordType.Expense)), Times.Once);

        // Assert: the persisted Settlement.Balance already reflects the fund credit
        // (house-1's balance was 500 before the fund draw — see the base test — minus the 400 Kč contribution).
        var houseOne = result.Single(s => s.HouseId == "house-1");
        houseOne.Balance.Should().Be(100m);
        houseOne.TotalAdvances.Should().Be(3400m); // 3000 original + 400 fund doplatek
    }

    [Fact]
    public async Task CloseAsync_FundDrawExceedsBalance_Throws_AndPersistsNothing()
    {
        // Arrange: no common-fund contributions at all -> fund balance is 0.
        SetupScenario();

        // Act
        var act = () => _sut.CloseAsync("period-1", LossAllocationMethod.Equal, fundDrawAmount: 100m);

        // Assert
        await act.Should().ThrowAsync<AppException>().WithMessage("*exceeds*");
        _advanceRepo.Verify(r => r.UpsertAsync(It.Is<AdvancePayment>(p => p.IsFundTransfer)), Times.Never);
        _financialRecordRepo.Verify(r => r.UpsertAsync(It.IsAny<FinancialRecord>()), Times.Never);
        _settlementRepo.Verify(r => r.UpsertAsync(It.IsAny<Settlement>()), Times.Never);
        _billingRepo.Verify(r => r.UpsertAsync(It.IsAny<BillingPeriod>()), Times.Never);
    }

    [Fact]
    public async Task CloseAsync_ApplyNewWaterPrice_UpdatesSettings_UsingRealizedPricePerM3()
    {
        // Arrange: SetupScenario has TotalInvoiceAmount=10000, TotalHouseConsumption=90, TotalLoss=10 -> 100 Kč/m3.
        SetupScenario();
        _settlementRepo.Setup(r => r.UpsertAsync(It.IsAny<Settlement>())).Returns(Task.CompletedTask);
        _billingRepo.Setup(r => r.UpsertAsync(It.IsAny<BillingPeriod>())).Returns(Task.CompletedTask);
        var validFrom = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        await _sut.CloseAsync("period-1", LossAllocationMethod.Equal,
            applyNewWaterPrice: true, newWaterPriceValidFrom: validFrom);

        // Assert
        _settingsRepo.Verify(r => r.UpsertAsync(It.Is<AdvanceSettings>(s =>
            s.WaterPricePerM3 == 100m && s.WaterPriceValidFrom == validFrom)), Times.Once);
    }

    [Fact]
    public async Task CloseAsync_ApplyNewWaterPrice_WithoutValidFromDate_Throws()
    {
        SetupScenario();

        var act = () => _sut.CloseAsync("period-1", LossAllocationMethod.Equal, applyNewWaterPrice: true);

        await act.Should().ThrowAsync<AppException>();
        _settingsRepo.Verify(r => r.UpsertAsync(It.IsAny<AdvanceSettings>()), Times.Never);
    }
}
