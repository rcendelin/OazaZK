using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByEntraObjectIdAsync(string entraObjectId);
}
