using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

public interface IFinancialRecordRepository : IRepository<FinancialRecord>
{
    Task<IReadOnlyList<FinancialRecord>> GetByYearAsync(int year);
    Task<IReadOnlyList<FinancialRecord>> GetByYearAndCategoryAsync(int year, string category);
}
