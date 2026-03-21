using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class UserRepository : TableStorageRepository<User>, IUserRepository
{
    public UserRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.Users)
    {
    }

    protected override TableEntity ToTableEntity(User entity) =>
        TableEntityMapper.ToTableEntity(entity);

    protected override User FromTableEntity(TableEntity tableEntity) =>
        TableEntityMapper.ToUser(tableEntity);

    public async Task<User?> GetByEmailAsync(string email)
    {
        var users = await GetByPartitionKeyAsync(PartitionKeys.User);
        return users.FirstOrDefault(u =>
            string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<User?> GetByEntraObjectIdAsync(string entraObjectId)
    {
        var users = await GetByPartitionKeyAsync(PartitionKeys.User);
        return users.FirstOrDefault(u =>
            string.Equals(u.EntraObjectId, entraObjectId, StringComparison.Ordinal));
    }
}
