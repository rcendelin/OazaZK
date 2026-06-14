using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

/// <summary>
/// Access to the singleton <see cref="AdvanceSettings"/> record
/// (PK="SETTINGS", RK="advances"). Returns a default instance when none exists.
/// </summary>
public interface IAdvanceSettingsRepository
{
    Task<AdvanceSettings> GetAsync();
    Task UpsertAsync(AdvanceSettings settings);
}
