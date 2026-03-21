namespace Oaza.Domain.Interfaces;

public interface IRepository<T> where T : class
{
    Task<T?> GetAsync(string partitionKey, string rowKey);
    Task<IReadOnlyList<T>> GetByPartitionKeyAsync(string partitionKey);
    Task<IReadOnlyList<T>> GetAllAsync();
    Task UpsertAsync(T entity);
    Task DeleteAsync(string partitionKey, string rowKey);
}
