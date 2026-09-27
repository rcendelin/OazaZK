namespace Oaza.Application.Interfaces;

/// <summary>
/// The last day fixed by an interim closing (T08). Changes of a component's rules and
/// participation must not reach into it. Until T08 exists nothing is closed.
/// </summary>
public interface IClosingBoundary
{
    /// <returns>The last closed day, or null when nothing is closed.</returns>
    Task<DateOnly?> GetLastClosedDayAsync();
}

/// <summary>Placeholder until interim closings (T08): nothing is closed.</summary>
public sealed class NoClosingBoundary : IClosingBoundary
{
    public Task<DateOnly?> GetLastClosedDayAsync() => Task.FromResult<DateOnly?>(null);
}
