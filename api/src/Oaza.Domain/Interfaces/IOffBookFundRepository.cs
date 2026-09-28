using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

/// <summary>
/// Off-book funds and their records (T10). Deliberately separate from everything else: only the off-book fund
/// module may use it, so fund money never reaches the saldo, closings, the cash book or the association's accounts.
/// </summary>
public interface IOffBookFundRepository
{
    Task<IReadOnlyList<OffBookFund>> GetFundsAsync();
    Task<OffBookFund?> GetFundAsync(string fundId);
    Task UpsertFundAsync(OffBookFund fund);
    Task<IReadOnlyList<FundRecord>> GetRecordsAsync(string fundId);
    Task<FundRecord?> GetRecordAsync(string fundId, string recordId);
    Task UpsertRecordAsync(FundRecord record);
    Task DeleteRecordAsync(string fundId, string recordId);
}
