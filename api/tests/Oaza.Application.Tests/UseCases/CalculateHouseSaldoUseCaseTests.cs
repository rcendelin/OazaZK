using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Oaza.Application.UseCases;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.UseCases;

public class CalculateHouseSaldoUseCaseTests
{
    private readonly Mock<IBillingPeriodRepository> _billingRepo = new();
    private readonly Mock<IHouseRepository> _houseRepo = new();
    private readonly Mock<IWaterMeterRepository> _meterRepo = new();
    private readonly Mock<IMeterReadingRepository> _readingRepo = new();
    private readonly Mock<ISupplierInvoiceRepository> _invoiceRepo = new();
    private readonly Mock<IAdvancePaymentRepository> _advanceRepo = new();
    private readonly Mock<IAdvanceSettingsRepository> _settingsRepo = new();
    private readonly Mock<ISettlementRepository> _settlementRepo = new();

    private readonly CalculateHouseSaldoUseCase _sut;

    private static readonly DateTime Start = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc);

    public CalculateHouseSaldoUseCaseTests()
    {
        _settingsRepo.Setup(r => r.GetAsync()).ReturnsAsync(new AdvanceSettings());

        var calc = new CalculateSettlementUseCase(
            _billingRepo.Object, _houseRepo.Object, _meterRepo.Object,
            _readingRepo.Object, _invoiceRepo.Object, _advanceRepo.Object,
            _settingsRepo.Object, Mock.Of<ILogger<CalculateSettlementUseCase>>());

        _sut = new CalculateHouseSaldoUseCase(
            calc, _billingRepo.Object, _settlementRepo.Object, _houseRepo.Object,
            _advanceRepo.Object, _settingsRepo.Object,
            Mock.Of<ILogger<CalculateHouseSaldoUseCase>>());

        _houseRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.House)).ReturnsAsync(new List<House>
        {
            new() { Id = "house-1", Name = "House A", IsActive = true },
        });
    }

    [Fact]
    public async Task CalculateAsync_ClosedPeriod_RecomputesPaidLive_SoPostCloseDoplatekCounts()
    {
        // Closed period with a stored settlement snapshot. Charges come from the
        // snapshot; the paid side is recomputed live from the ledger — including a
        // doplatek added AFTER the period closed (so it cannot be in the snapshot's
        // TotalAdvances of 3000).
        _billingRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Period)).ReturnsAsync(new List<BillingPeriod>
        {
            new() { Id = "p1", Name = "H1 2025", DateFrom = Start, DateTo = End, Status = BillingPeriodStatus.Closed },
        });

        _settlementRepo.Setup(r => r.GetByPeriodIdAsync("p1")).ReturnsAsync(new List<Settlement>
        {
            new()
            {
                PeriodId = "p1", HouseId = "house-1",
                CalculatedAmount = 3500m, TotalAdvances = 3000m, Balance = 500m,
                ElectricityCharge = 1200m, ElectricityAdvances = 1000m,
                CommonCharge = 600m, CommonAdvances = 600m,
            },
        });

        _advanceRepo.Setup(r => r.GetByHouseIdAsync("house-1")).ReturnsAsync(new List<AdvancePayment>
        {
            // Regular advance paid during the period.
            new() { HouseId = "house-1", Year = 2025, Month = 3, Type = PaymentType.Advance,
                    WaterAmount = 3000m, ElectricityAmount = 1000m, CommonAmount = 600m,
                    PaymentDate = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc) },
            // Doplatek dated inside the period, recorded after close.
            new() { HouseId = "house-1", RowKey = "D-x", Type = PaymentType.Doplatek,
                    WaterAmount = 500m, PaymentDate = new DateTime(2025, 4, 20, 0, 0, 0, DateTimeKind.Utc) },
        });

        var result = await _sut.CalculateAsync(null);

        result.Should().HaveCount(1);
        var s = result[0];
        // Water: charged 3500, paid 3000 + 500 = 3500 -> saldo 0 (proves live recompute).
        s.Water.Charged.Should().Be(3500m);
        s.Water.Paid.Should().Be(3500m);
        s.Water.Saldo.Should().Be(0m);
        // Electricity: charged 1200, paid 1000 -> 200 nedoplatek.
        s.Electricity.Saldo.Should().Be(200m);
        // Common: charged 600, paid 600 -> 0.
        s.Common.Saldo.Should().Be(0m);
        s.TotalSaldo.Should().Be(200m);
    }

    [Fact]
    public async Task CalculateAsync_PaymentOutsideAllPeriods_BecomesUnassignedCredit()
    {
        // No periods at all — a doplatek becomes pure credit (negative saldo).
        _billingRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Period))
            .ReturnsAsync(new List<BillingPeriod>());

        _advanceRepo.Setup(r => r.GetByHouseIdAsync("house-1")).ReturnsAsync(new List<AdvancePayment>
        {
            new() { HouseId = "house-1", RowKey = "D-y", Type = PaymentType.Doplatek,
                    WaterAmount = 100m, PaymentDate = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc) },
        });

        var result = await _sut.CalculateAsync(null);

        var s = result[0];
        s.Water.Charged.Should().Be(0m);
        s.Water.Paid.Should().Be(100m);
        s.Water.Saldo.Should().Be(-100m); // přeplatek
        s.TotalSaldo.Should().Be(-100m);
        s.Periods.Should().ContainSingle(p => p.PeriodName == "Nezařazené platby");
    }

    [Fact]
    public async Task CalculateAsync_Payout_ReducesOverpayment_AsNetAdjustment()
    {
        _billingRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Period))
            .ReturnsAsync(new List<BillingPeriod>());

        _advanceRepo.Setup(r => r.GetByHouseIdAsync("house-1")).ReturnsAsync(new List<AdvancePayment>
        {
            // Overpaid 1000 (credit), then 600 paid back.
            new() { HouseId = "house-1", RowKey = "D-1", Type = PaymentType.Doplatek,
                    WaterAmount = 1000m, PaymentDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new() { HouseId = "house-1", RowKey = "V-1", Type = PaymentType.Payout,
                    Amount = 600m, PaymentDate = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc) },
        });

        var s = (await _sut.CalculateAsync(null))[0];

        s.ComponentSaldo.Should().Be(-1000m); // credit from the doplatek
        s.NetAdjustments.Should().Be(600m);    // payout pulls saldo back toward zero
        s.TotalSaldo.Should().Be(-400m);       // -1000 + 600
        s.Adjustments.Should().ContainSingle(a => a.Type == "Payout" && a.Amount == 600m);
    }

    [Fact]
    public async Task CalculateAsync_OpeningCredit_SetsSaldo_MonthsCovered_AndDissolvingFlag()
    {
        _houseRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.House)).ReturnsAsync(new List<House>
        {
            new() { Id = "house-1", Name = "House A", IsActive = true, DissolveOverpayment = true },
        });
        _billingRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Period))
            .ReturnsAsync(new List<BillingPeriod>());
        _settingsRepo.Setup(r => r.GetAsync()).ReturnsAsync(new AdvanceSettings
        {
            HouseOverrides = new Dictionary<string, HouseAdvanceOverride>
            {
                ["house-1"] = new() { WaterAdvance = 600m, ElectricityAdvance = 300m, CommonAdvance = 100m },
            },
        });

        // Opening overpayment 3000 → stored as signed -3000.
        _advanceRepo.Setup(r => r.GetByHouseIdAsync("house-1")).ReturnsAsync(new List<AdvancePayment>
        {
            new() { HouseId = "house-1", RowKey = "O-1", Type = PaymentType.OpeningBalance,
                    Amount = -3000m, PaymentDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Note = "počáteční přeplatek" },
        });

        var s = (await _sut.CalculateAsync(null))[0];

        s.TotalSaldo.Should().Be(-3000m);        // přeplatek
        s.PrescribedMonthly.Should().Be(1000m);   // 600 + 300 + 100
        s.MonthsCovered.Should().Be(3.0m);        // 3000 / 1000
        s.Dissolving.Should().BeTrue();
        s.Adjustments.Should().ContainSingle(a => a.Type == "OpeningBalance" && a.Amount == -3000m);
    }
}
