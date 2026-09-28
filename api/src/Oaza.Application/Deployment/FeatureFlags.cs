namespace Oaza.Application.Deployment;

/// <summary>
/// Feature flags from app settings (read in <c>Program.cs</c>). <c>OFF_BOOK_FUND_ENABLED</c> (T10, O4): the off-book
/// fund stays off until its legal and tax treatment is settled — default false.
/// </summary>
public class FeatureFlags
{
    public const string OffBookFundKey = "OFF_BOOK_FUND_ENABLED";

    public FeatureFlags(bool offBookFundEnabled) => OffBookFundEnabled = offBookFundEnabled;

    public bool OffBookFundEnabled { get; }

    /// <summary>A flag value from configuration: only „true“ (any case) turns it on.</summary>
    public static bool IsOn(string? value) => bool.TryParse(value, out var on) && on;
}
