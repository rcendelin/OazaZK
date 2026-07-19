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

public class CalculateSettlementUseCaseTests
{
    private readonly Mock<IBillingPeriodRepository> _billingPeriodRepo = new();
    private readonly Mock<IHouseRepository> _houseRepo = new();
    private readonly Mock<IWaterMeterRepository> _meterRepo = new();
    private readonly Mock<IMeterReadingRepository> _readingRepo = new();
    private readonly Mock<ISupplierInvoiceRepository> _invoiceRepo = new();
    private readonly Mock<IAdvancePaymentRepository> _advanceRepo = new();
    private readonly Mock<IAdvanceSettingsRepository> _settingsRepo = new();
    private readonly Mock<ILogger<CalculateSettlementUseCase>> _logger = new();

    private readonly CalculateSettlementUseCase _sut;

    private static readonly DateTime PeriodStart = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc);

    public CalculateSettlementUseCaseTests()
    {
        // Default settings: no electricity/common charges so water assertions are isolated.
        _settingsRepo.Setup(r => r.GetAsync()).ReturnsAsync(new AdvanceSettings());

        _sut = new CalculateSettlementUseCase(
            _billingPeriodRepo.Object,
            _houseRepo.Object,
            _meterRepo.Object,
            _readingRepo.Object,
            _invoiceRepo.Object,
            _advanceRepo.Object,
            _settingsRepo.Object,
            _logger.Object);
    }

    [Fact]
    public async Task CalculateAsync_BasicTwoHouses_EqualLoss_ReturnsCorrectSettlement()
    {
        // Arrange
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A", "house-2", "House B");
        SetupMeters("main-meter", "meter-1", "house-1", "meter-2", "house-2");

        // Main meter: 100 -> 200 = 100 m³
        SetupReadings("main-meter", (PeriodStart, 100m), (PeriodEnd, 200m));
        // House A: 50 -> 80 = 30 m³
        SetupReadings("meter-1", (PeriodStart, 50m), (PeriodEnd, 80m));
        // House B: 20 -> 80 = 60 m³
        SetupReadings("meter-2", (PeriodStart, 20m), (PeriodEnd, 80m));

        // Total house consumption = 90, Loss = 10, loss per house = 5
        SetupInvoices(10000m); // 10,000 CZK total
        SetupAdvances("house-1", 3000m);
        SetupAdvances("house-2", 5000m);

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.Equal);

        // Assert
        result.MainMeterConsumption.Should().Be(100m);
        result.TotalHouseConsumption.Should().Be(90m);
        result.TotalLoss.Should().Be(10m);
        result.TotalInvoiceAmount.Should().Be(10000m);
        result.Houses.Should().HaveCount(2);

        // House A: consumption=30, loss=5, share = (30+5)/(90+10) = 35%
        var houseA = result.Houses.First(h => h.HouseId == "house-1");
        houseA.ConsumptionM3.Should().Be(30m);
        houseA.LossAllocatedM3.Should().Be(5m);
        houseA.SharePercent.Should().Be(35m);
        houseA.CalculatedAmount.Should().Be(3500m); // 35% of 10000
        houseA.TotalAdvances.Should().Be(3000m);
        houseA.Balance.Should().Be(500m); // underpayment

        // House B: consumption=60, loss=5, share = (60+5)/(90+10) = 65%
        var houseB = result.Houses.First(h => h.HouseId == "house-2");
        houseB.ConsumptionM3.Should().Be(60m);
        houseB.LossAllocatedM3.Should().Be(5m);
        houseB.SharePercent.Should().Be(65m);
        houseB.CalculatedAmount.Should().Be(6500m); // 65% of 10000
        houseB.TotalAdvances.Should().Be(5000m);
        houseB.Balance.Should().Be(1500m); // underpayment
    }

    [Fact]
    public async Task CalculateAsync_ReadingsOutsidePeriod_UsesClosestReadingsWithinBounds()
    {
        // Arrange: meters have readings before the period start and after the period
        // end. Consumption must use the latest reading <= start and the latest <= end
        // (the post-period reading must be ignored).
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A", "house-2", "House B");
        SetupMeters("main-meter", "meter-1", "house-1", "meter-2", "house-2");

        var beforeStart = PeriodStart.AddDays(-20);
        var insidePeriod = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var afterEnd = PeriodEnd.AddDays(20);

        // Main: 100 (<=start) -> 160 (<=end); 999 (after end) must be ignored => 60
        SetupReadings("main-meter", (beforeStart, 100m), (insidePeriod, 160m), (afterEnd, 999m));
        SetupReadings("meter-1", (beforeStart, 50m), (insidePeriod, 80m));   // 30
        SetupReadings("meter-2", (beforeStart, 20m), (insidePeriod, 50m));   // 30

        SetupInvoices(1000m);
        SetupAdvances("house-1", 0m);
        SetupAdvances("house-2", 0m);

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.Equal);

        // Assert
        result.MainMeterConsumption.Should().Be(60m);
        result.TotalHouseConsumption.Should().Be(60m);
        result.TotalLoss.Should().Be(0m);
    }

    [Fact]
    public async Task CalculateAsync_NoReadingBeforeStart_FallsBackToEarliestReading()
    {
        // Arrange: every reading is strictly after the period start, so the baseline
        // start reading falls back to the earliest available reading.
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A", "house-2", "House B");
        SetupMeters("main-meter", "meter-1", "house-1", "meter-2", "house-2");

        var early = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var late = new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        SetupReadings("main-meter", (early, 200m), (late, 260m)); // 60
        SetupReadings("meter-1", (early, 50m), (late, 80m));      // 30
        SetupReadings("meter-2", (early, 20m), (late, 50m));      // 30

        SetupInvoices(1000m);
        SetupAdvances("house-1", 0m);
        SetupAdvances("house-2", 0m);

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.Equal);

        // Assert: earliest reading used as the start baseline
        result.MainMeterConsumption.Should().Be(60m);
        result.TotalHouseConsumption.Should().Be(60m);
    }

    [Fact]
    public async Task CalculateAsync_ProportionalLossAllocation_AllocatesProportionally()
    {
        // Arrange
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A", "house-2", "House B");
        SetupMeters("main-meter", "meter-1", "house-1", "meter-2", "house-2");

        // Main meter: 100 m³
        SetupReadings("main-meter", (PeriodStart, 0m), (PeriodEnd, 100m));
        // House A: 30 m³ (1/3 of total)
        SetupReadings("meter-1", (PeriodStart, 0m), (PeriodEnd, 30m));
        // House B: 60 m³ (2/3 of total)
        SetupReadings("meter-2", (PeriodStart, 0m), (PeriodEnd, 60m));

        // Loss = 10 m³
        // Proportional: House A = 10 * (30/90) = 3.333, House B = 10 * (60/90) = 6.667
        SetupInvoices(9000m);
        SetupAdvances("house-1", 0m);
        SetupAdvances("house-2", 0m);

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.ProportionalToConsumption);

        // Assert
        result.LossAllocationMethod.Should().Be("ProportionalToConsumption");

        var houseA = result.Houses.First(h => h.HouseId == "house-1");
        houseA.LossAllocatedM3.Should().BeApproximately(3.333m, 0.001m);
        // Share = (30 + 3.333) / 100 = 33.333%
        houseA.SharePercent.Should().BeApproximately(33.33m, 0.01m);

        var houseB = result.Houses.First(h => h.HouseId == "house-2");
        houseB.LossAllocatedM3.Should().BeApproximately(6.667m, 0.001m);
        houseB.SharePercent.Should().BeApproximately(66.67m, 0.01m);
    }

    [Fact]
    public async Task CalculateAsync_NoLoss_AllLossFieldsAreZero()
    {
        // Arrange
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A", "house-2", "House B");
        SetupMeters("main-meter", "meter-1", "house-1", "meter-2", "house-2");

        // Main = sum of houses = no loss
        SetupReadings("main-meter", (PeriodStart, 0m), (PeriodEnd, 100m));
        SetupReadings("meter-1", (PeriodStart, 0m), (PeriodEnd, 40m));
        SetupReadings("meter-2", (PeriodStart, 0m), (PeriodEnd, 60m));

        SetupInvoices(5000m);
        SetupAdvances("house-1", 2000m);
        SetupAdvances("house-2", 3000m);

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.Equal);

        // Assert
        result.TotalLoss.Should().Be(0m);
        result.Houses.Should().OnlyContain(h => h.LossAllocatedM3 == 0m);

        var houseA = result.Houses.First(h => h.HouseId == "house-1");
        houseA.SharePercent.Should().Be(40m);
        houseA.CalculatedAmount.Should().Be(2000m);
        houseA.Balance.Should().Be(0m); // exactly matches advances
    }

    [Fact]
    public async Task CalculateAsync_ZeroHouseConsumption_EqualSplit()
    {
        // Arrange
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A", "house-2", "House B");
        SetupMeters("main-meter", "meter-1", "house-1", "meter-2", "house-2");

        // Main has consumption, but houses have zero (all loss)
        SetupReadings("main-meter", (PeriodStart, 0m), (PeriodEnd, 10m));
        SetupReadings("meter-1", (PeriodStart, 50m), (PeriodEnd, 50m));
        SetupReadings("meter-2", (PeriodStart, 100m), (PeriodEnd, 100m));

        SetupInvoices(1000m);
        SetupAdvances("house-1", 0m);
        SetupAdvances("house-2", 0m);

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.Equal);

        // Assert: totalHouseConsumption=0, loss=10, equal loss split=5 each
        // Share = (0+5)/(0+10) = 50% each
        result.Houses.Should().OnlyContain(h => h.SharePercent == 50m);
        result.Houses.Should().OnlyContain(h => h.CalculatedAmount == 500m);
    }

    [Fact]
    public async Task CalculateAsync_MissingMainMeterReading_ThrowsAppException()
    {
        // Arrange
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A");
        SetupMeters("main-meter", "meter-1", "house-1");

        // No readings for main meter
        _readingRepo.Setup(r => r.GetByMeterIdAsync("main-meter"))
            .ReturnsAsync(new List<MeterReading>());
        SetupReadings("meter-1", (PeriodStart, 0m), (PeriodEnd, 10m));

        SetupInvoices(1000m);
        SetupAdvances("house-1", 0m);

        // Act & Assert
        await _sut.Invoking(s => s.CalculateAsync(periodId, LossAllocationMethod.Equal))
            .Should().ThrowAsync<AppException>()
            .WithMessage("*Nebyly nalezeny žádné odečty*Main meter*");
    }

    [Fact]
    public async Task CalculateAsync_MissingHouseMeter_SkipsHouse()
    {
        // Arrange
        var periodId = "period-1";
        SetupOpenPeriod(periodId);

        // Two houses but only one has a meter
        _houseRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.House))
            .ReturnsAsync(new List<House>
            {
                new() { Id = "house-1", Name = "House A", IsActive = true },
                new() { Id = "house-2", Name = "House B (no meter)", IsActive = true },
            });

        // Only main meter + house-1 meter, no meter for house-2
        _meterRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Meter))
            .ReturnsAsync(new List<WaterMeter>
            {
                new() { Id = "main-meter", Type = MeterType.Main, HouseId = null },
                new() { Id = "meter-1", Type = MeterType.Individual, HouseId = "house-1" },
            });

        SetupReadings("main-meter", (PeriodStart, 0m), (PeriodEnd, 50m));
        SetupReadings("meter-1", (PeriodStart, 0m), (PeriodEnd, 40m));

        SetupInvoices(2000m);
        SetupAdvances("house-1", 1000m);

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.Equal);

        // Assert: only house-1 in results
        result.Houses.Should().HaveCount(1);
        result.Houses[0].HouseId.Should().Be("house-1");
    }

    [Fact]
    public async Task CalculateAsync_AdvancePaymentAggregation_SumsCorrectly()
    {
        // Arrange
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A");
        SetupMeters("main-meter", "meter-1", "house-1");

        SetupReadings("main-meter", (PeriodStart, 0m), (PeriodEnd, 50m));
        SetupReadings("meter-1", (PeriodStart, 0m), (PeriodEnd, 50m));

        SetupInvoices(3000m);

        // Multiple advance payments (water component drives the water balance)
        _advanceRepo.Setup(r => r.GetByHouseAndPeriodAsync("house-1", PeriodStart, PeriodEnd))
            .ReturnsAsync(new List<AdvancePayment>
            {
                new() { HouseId = "house-1", Year = 2025, Month = 1, Amount = 500m, WaterAmount = 500m },
                new() { HouseId = "house-1", Year = 2025, Month = 2, Amount = 500m, WaterAmount = 500m },
                new() { HouseId = "house-1", Year = 2025, Month = 3, Amount = 500m, WaterAmount = 500m },
            });

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.Equal);

        // Assert
        var house = result.Houses[0];
        house.TotalAdvances.Should().Be(1500m);
        house.CalculatedAmount.Should().Be(3000m);
        house.Balance.Should().Be(1500m); // underpayment: 3000 - 1500
    }

    [Fact]
    public async Task CalculateAsync_Overpayment_NegativeBalance()
    {
        // Arrange
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A");
        SetupMeters("main-meter", "meter-1", "house-1");

        SetupReadings("main-meter", (PeriodStart, 0m), (PeriodEnd, 50m));
        SetupReadings("meter-1", (PeriodStart, 0m), (PeriodEnd, 50m));

        SetupInvoices(1000m);
        SetupAdvances("house-1", 2000m); // Paid more than owed

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.Equal);

        // Assert: overpayment = negative balance (přeplatek)
        result.Houses[0].Balance.Should().Be(-1000m);
    }

    [Fact]
    public async Task CalculateAsync_ClosedPeriod_ThrowsAppException()
    {
        // Arrange
        _billingPeriodRepo.Setup(r => r.GetAsync(PartitionKeys.Period, "period-1"))
            .ReturnsAsync(new BillingPeriod
            {
                Id = "period-1",
                Name = "Closed Period",
                DateFrom = PeriodStart,
                DateTo = PeriodEnd,
                Status = BillingPeriodStatus.Closed,
            });

        // Act & Assert
        await _sut.Invoking(s => s.CalculateAsync("period-1", LossAllocationMethod.Equal))
            .Should().ThrowAsync<AppException>()
            .WithMessage("*již uzavřeno*");
    }

    [Fact]
    public async Task CalculateAsync_NegativeLoss_TreatedAsZero()
    {
        // Arrange: house meters sum exceeds main meter (meter error)
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A");
        SetupMeters("main-meter", "meter-1", "house-1");

        // Main = 40, House = 50 => negative loss
        SetupReadings("main-meter", (PeriodStart, 0m), (PeriodEnd, 40m));
        SetupReadings("meter-1", (PeriodStart, 0m), (PeriodEnd, 50m));

        SetupInvoices(1000m);
        SetupAdvances("house-1", 500m);

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.Equal);

        // Assert: loss clamped to 0
        result.TotalLoss.Should().Be(0m);
        result.Houses[0].LossAllocatedM3.Should().Be(0m);
        result.Houses[0].SharePercent.Should().Be(100m);
        result.Houses[0].CalculatedAmount.Should().Be(1000m);
    }

    [Fact]
    public async Task CalculateAsync_ProportionalWithZeroConsumption_FallsBackToEqual()
    {
        // Arrange
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A", "house-2", "House B");
        SetupMeters("main-meter", "meter-1", "house-1", "meter-2", "house-2");

        SetupReadings("main-meter", (PeriodStart, 0m), (PeriodEnd, 10m));
        SetupReadings("meter-1", (PeriodStart, 0m), (PeriodEnd, 0m));
        SetupReadings("meter-2", (PeriodStart, 0m), (PeriodEnd, 0m));

        SetupInvoices(2000m);
        SetupAdvances("house-1", 0m);
        SetupAdvances("house-2", 0m);

        // Act: proportional with zero consumption should fall back to equal
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.ProportionalToConsumption);

        // Assert: equal split since totalHouseConsumption = 0
        result.Houses.Should().OnlyContain(h => h.LossAllocatedM3 == 5m);
        result.Houses.Should().OnlyContain(h => h.SharePercent == 50m);
    }

    [Fact]
    public async Task CalculateAsync_NoInvoices_TotalIsZero()
    {
        // Arrange: period with consumption but no invoices
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A", "house-2", "House B");
        SetupMeters("main-meter", "meter-1", "house-1", "meter-2", "house-2");

        SetupReadings("main-meter", (PeriodStart, 0m), (PeriodEnd, 100m));
        SetupReadings("meter-1", (PeriodStart, 0m), (PeriodEnd, 40m));
        SetupReadings("meter-2", (PeriodStart, 0m), (PeriodEnd, 60m));

        // No invoices
        _invoiceRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Invoice))
            .ReturnsAsync(new List<SupplierInvoice>());

        SetupAdvances("house-1", 1000m);
        SetupAdvances("house-2", 2000m);

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.Equal);

        // Assert
        result.TotalInvoiceAmount.Should().Be(0m);
        result.Houses.Should().HaveCount(2);

        // All calculated amounts should be 0 since no invoices
        result.Houses.Should().OnlyContain(h => h.CalculatedAmount == 0m);

        // Balances should be negative (overpayment) since advances > 0 but amount = 0
        var houseA = result.Houses.First(h => h.HouseId == "house-1");
        houseA.Balance.Should().Be(-1000m);

        var houseB = result.Houses.First(h => h.HouseId == "house-2");
        houseB.Balance.Should().Be(-2000m);
    }

    [Fact]
    public async Task CalculateAsync_SingleHouse_Gets100PercentShare()
    {
        // Arrange: single house scenario
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A");
        SetupMeters("main-meter", "meter-1", "house-1");

        SetupReadings("main-meter", (PeriodStart, 0m), (PeriodEnd, 100m));
        SetupReadings("meter-1", (PeriodStart, 0m), (PeriodEnd, 80m));

        // Loss = 20 m³, all allocated to single house
        SetupInvoices(5000m);
        SetupAdvances("house-1", 4000m);

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.Equal);

        // Assert
        result.Houses.Should().HaveCount(1);
        var house = result.Houses[0];

        house.ConsumptionM3.Should().Be(80m);
        house.LossAllocatedM3.Should().Be(20m);
        house.SharePercent.Should().Be(100m);
        house.CalculatedAmount.Should().Be(5000m);
        house.TotalAdvances.Should().Be(4000m);
        house.Balance.Should().Be(1000m); // underpayment
    }

    [Fact]
    public async Task CalculateAsync_SingleHouse_ProportionalLoss_AlsoGets100Percent()
    {
        // Arrange: single house with proportional loss allocation
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A");
        SetupMeters("main-meter", "meter-1", "house-1");

        SetupReadings("main-meter", (PeriodStart, 0m), (PeriodEnd, 100m));
        SetupReadings("meter-1", (PeriodStart, 0m), (PeriodEnd, 80m));

        SetupInvoices(5000m);
        SetupAdvances("house-1", 5000m);

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.ProportionalToConsumption);

        // Assert: with proportional, single house gets all the loss too
        result.Houses.Should().HaveCount(1);
        var house = result.Houses[0];

        house.LossAllocatedM3.Should().Be(20m);
        house.SharePercent.Should().Be(100m);
        house.Balance.Should().Be(0m); // exact match
    }

    [Fact]
    public async Task CalculateAsync_InvoiceWithLineItems_UsesOnlyLinesInPeriod_InclVat()
    {
        // Arrange: one invoice with two sub-readings; only the 2025-03 line falls in
        // the billing period (2025-01..2025-06). VAT 12 % must be applied.
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A");
        SetupMeters("main-meter", "meter-1", "house-1");
        SetupReadings("main-meter", (PeriodStart, 0m), (PeriodEnd, 50m));
        SetupReadings("meter-1", (PeriodStart, 0m), (PeriodEnd, 50m));
        SetupAdvances("house-1", 0m);

        _invoiceRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Invoice))
            .ReturnsAsync(new List<SupplierInvoice>
            {
                new()
                {
                    Id = "inv-1",
                    InvoiceNumber = "FA-1",
                    VatRatePercent = 12m,
                    LineItems = new List<InvoiceLineItem>
                    {
                        new() { DateFrom = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc), DateTo = new DateTime(2025, 3, 31, 0, 0, 0, DateTimeKind.Utc), AmountExclVat = 1000m, ConsumptionM3 = 10m },
                        new() { DateFrom = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc), DateTo = new DateTime(2024, 3, 31, 0, 0, 0, DateTimeKind.Utc), AmountExclVat = 5000m, ConsumptionM3 = 50m },
                    },
                },
            });

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.Equal);

        // Assert: only the in-period line, incl. 12% VAT → 1000 * 1.12 = 1120
        result.TotalInvoiceAmount.Should().Be(1120m);
        result.Houses.Should().HaveCount(1);
        result.Houses[0].CalculatedAmount.Should().Be(1120m); // single house = 100 %
    }

    [Fact]
    public async Task CalculateAsync_ElectricityAndCommonCharges_AccrueOverPeriodMonths()
    {
        // Arrange: period spans 6 months (Jan–Jun 2025). Electricity 1200/mo at 100%
        // coefficient and common base 200/mo accrue over those 6 months.
        var periodId = "period-1";
        SetupOpenPeriod(periodId);
        SetupHouses("house-1", "House A");
        SetupMeters("main-meter", "meter-1", "house-1");
        SetupReadings("main-meter", (PeriodStart, 0m), (PeriodEnd, 50m));
        SetupReadings("meter-1", (PeriodStart, 0m), (PeriodEnd, 50m));
        SetupInvoices(3000m);

        _settingsRepo.Setup(r => r.GetAsync()).ReturnsAsync(new AdvanceSettings
        {
            MonthlyElectricityCost = 1200m,
            MonthlyCommonBaseFee = 200m,
            ElectricityCoefficients = new Dictionary<string, decimal> { ["house-1"] = 100m },
        });

        // Payment split across all three components.
        _advanceRepo.Setup(r => r.GetByHouseAndPeriodAsync("house-1", PeriodStart, PeriodEnd))
            .ReturnsAsync(new List<AdvancePayment>
            {
                new() { HouseId = "house-1", Year = 2025, Month = 3, Amount = 1600m, WaterAmount = 1000m, ElectricityAmount = 500m, CommonAmount = 100m },
            });

        // Act
        var result = await _sut.CalculateAsync(periodId, LossAllocationMethod.Equal);

        // Assert
        result.MonthsInPeriod.Should().Be(6);
        var house = result.Houses[0];
        house.CalculatedAmount.Should().Be(3000m);   // water charge
        house.TotalAdvances.Should().Be(1000m);       // water advances only
        house.Balance.Should().Be(2000m);             // 3000 - 1000
        house.ElectricityCharge.Should().Be(7200m);   // 1200 * 100% * 6
        house.ElectricityAdvances.Should().Be(500m);
        house.CommonCharge.Should().Be(1200m);        // 200 * 6
        house.CommonAdvances.Should().Be(100m);
        result.TotalElectricityCharge.Should().Be(7200m);
        result.TotalCommonCharge.Should().Be(1200m);
    }

    [Theory]
    [InlineData(2025, 1, 2025, 6, 6)]   // Jan–Jun inclusive
    [InlineData(2025, 1, 2025, 1, 1)]   // single month
    [InlineData(2025, 1, 2025, 12, 12)] // full year
    [InlineData(2023, 11, 2026, 6, 32)] // multi-year span
    public void MonthsInPeriod_CountsInclusiveMonths(int fy, int fm, int ty, int tm, int expected)
    {
        var from = new DateTime(fy, fm, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(ty, tm, 28, 0, 0, 0, DateTimeKind.Utc);
        CalculateSettlementUseCase.MonthsInPeriod(from, to).Should().Be(expected);
    }

    #region Test Setup Helpers

    private void SetupOpenPeriod(string periodId)
    {
        _billingPeriodRepo.Setup(r => r.GetAsync(PartitionKeys.Period, periodId))
            .ReturnsAsync(new BillingPeriod
            {
                Id = periodId,
                Name = "Test Period",
                DateFrom = PeriodStart,
                DateTo = PeriodEnd,
                Status = BillingPeriodStatus.Open,
            });
    }

    private void SetupHouses(params string[] houseIdNamePairs)
    {
        var houses = new List<House>();
        for (var i = 0; i < houseIdNamePairs.Length; i += 2)
        {
            houses.Add(new House
            {
                Id = houseIdNamePairs[i],
                Name = houseIdNamePairs[i + 1],
                IsActive = true,
            });
        }

        _houseRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.House))
            .ReturnsAsync(houses);
    }

    /// <summary>
    /// Sets up meters. First param is main meter ID, then pairs of (meterId, houseId).
    /// </summary>
    private void SetupMeters(string mainMeterId, params string[] meterHousePairs)
    {
        var meters = new List<WaterMeter>
        {
            new() { Id = mainMeterId, Type = MeterType.Main, HouseId = null },
        };

        for (var i = 0; i < meterHousePairs.Length; i += 2)
        {
            meters.Add(new WaterMeter
            {
                Id = meterHousePairs[i],
                Type = MeterType.Individual,
                HouseId = meterHousePairs[i + 1],
            });
        }

        _meterRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Meter))
            .ReturnsAsync(meters);
    }

    private void SetupReadings(string meterId, params (DateTime date, decimal value)[] readings)
    {
        var readingEntities = readings.Select(r => new MeterReading
        {
            MeterId = meterId,
            ReadingDate = r.date,
            Value = r.value,
            Source = ReadingSource.Manual,
            ImportedAt = DateTime.UtcNow,
            ImportedBy = "test-user",
        }).ToList();

        _readingRepo.Setup(r => r.GetByMeterIdAsync(meterId))
            .ReturnsAsync(readingEntities);
    }

    private void SetupInvoices(decimal totalAmount)
    {
        _invoiceRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Invoice))
            .ReturnsAsync(new List<SupplierInvoice>
            {
                new()
                {
                    Id = "inv-1",
                    Year = 2025,
                    Month = 3,
                    InvoiceNumber = "INV-001",
                    Amount = totalAmount,
                    ConsumptionM3 = 100m,
                },
            });
    }

    private void SetupAdvances(string houseId, decimal totalAmount)
    {
        var advances = totalAmount > 0
            ? new List<AdvancePayment>
            {
                new()
                {
                    HouseId = houseId,
                    Year = 2025,
                    Month = 3,
                    Amount = totalAmount,
                    WaterAmount = totalAmount,
                    PaymentDate = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                },
            }
            : new List<AdvancePayment>();

        _advanceRepo.Setup(r => r.GetByHouseAndPeriodAsync(houseId, PeriodStart, PeriodEnd))
            .ReturnsAsync(advances);
    }

    #endregion
}
