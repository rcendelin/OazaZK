using Oaza.Domain.Constants;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.UseCases;

/// <summary>
/// Computes the common fund balance: money collected toward the common base
/// across all households, minus extraordinary expenses (anything that isn't
/// water or electricity, which are settled through their own contributions).
/// Shared by the read endpoint (GET /finance/fund) and the water-settlement
/// close flow, which can draw part of this balance into the water vyúčtování.
/// </summary>
public class GetFundBalanceUseCase
{
    private readonly IHouseRepository _houseRepository;
    private readonly IAdvancePaymentRepository _advanceRepository;
    private readonly IFinancialRecordRepository _financialRecordRepository;

    public GetFundBalanceUseCase(
        IHouseRepository houseRepository,
        IAdvancePaymentRepository advanceRepository,
        IFinancialRecordRepository financialRecordRepository)
    {
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _advanceRepository = advanceRepository ?? throw new ArgumentNullException(nameof(advanceRepository));
        _financialRecordRepository = financialRecordRepository ?? throw new ArgumentNullException(nameof(financialRecordRepository));
    }

    public async Task<FundBalanceResult> CalculateAsync()
    {
        var houses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
        decimal commonContributions = 0m;
        foreach (var house in houses)
        {
            var payments = await _advanceRepository.GetByHouseIdAsync(house.Id);
            commonContributions += payments
                .Where(p => p.Type is PaymentType.Advance or PaymentType.Doplatek)
                .Sum(p => p.CommonAmount);
        }

        var records = await _financialRecordRepository.GetAllAsync();
        var extraordinaryCosts = records
            .Where(r => r.Type == FinancialRecordType.Expense
                && !string.Equals(r.Category, "voda", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(r.Category, "elektro", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.Amount);

        return new FundBalanceResult(
            Math.Round(commonContributions, 2),
            Math.Round(extraordinaryCosts, 2),
            Math.Round(commonContributions - extraordinaryCosts, 2));
    }
}

public record FundBalanceResult(decimal CommonContributions, decimal ExtraordinaryCosts, decimal FundBalance);
