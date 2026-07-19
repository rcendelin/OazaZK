using FluentAssertions;
using Moq;
using Oaza.Application.UseCases;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.UseCases;

public class GetFundBalanceUseCaseTests
{
    private readonly Mock<IHouseRepository> _houseRepo = new();
    private readonly Mock<IAdvancePaymentRepository> _advanceRepo = new();
    private readonly Mock<IFinancialRecordRepository> _financialRecordRepo = new();
    private readonly GetFundBalanceUseCase _sut;

    public GetFundBalanceUseCaseTests()
    {
        _sut = new GetFundBalanceUseCase(_houseRepo.Object, _advanceRepo.Object, _financialRecordRepo.Object);
    }

    [Fact]
    public async Task CalculateAsync_SumsCommonContributions_MinusNonWaterElectroExpenses()
    {
        _houseRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.House)).ReturnsAsync(new List<House>
        {
            new() { Id = "house-1", Name = "A", IsActive = true },
            new() { Id = "house-2", Name = "B", IsActive = false },
        });
        _advanceRepo.Setup(r => r.GetByHouseIdAsync("house-1")).ReturnsAsync(new List<AdvancePayment>
        {
            new() { HouseId = "house-1", Type = PaymentType.Advance, CommonAmount = 500m },
        });
        _advanceRepo.Setup(r => r.GetByHouseIdAsync("house-2")).ReturnsAsync(new List<AdvancePayment>
        {
            new() { HouseId = "house-2", Type = PaymentType.Doplatek, CommonAmount = 300m },
            new() { HouseId = "house-2", Type = PaymentType.Payout, CommonAmount = 1000m }, // excluded: Payout is net-level
        });
        _financialRecordRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<FinancialRecord>
        {
            new() { Type = FinancialRecordType.Expense, Category = "udrzba", Amount = 200m },
            new() { Type = FinancialRecordType.Expense, Category = "voda", Amount = 9999m }, // excluded: water settled separately
            new() { Type = FinancialRecordType.Income, Category = "jine", Amount = 9999m }, // excluded: not an expense
        });

        var result = await _sut.CalculateAsync();

        result.CommonContributions.Should().Be(800m);
        result.ExtraordinaryCosts.Should().Be(200m);
        result.FundBalance.Should().Be(600m);
    }
}
