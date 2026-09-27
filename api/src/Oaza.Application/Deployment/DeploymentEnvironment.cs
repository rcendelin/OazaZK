namespace Oaza.Application.Deployment;

/// <summary>
/// Which deployment this backend serves, from the <c>Environment</c> app setting
/// (<c>dev</c> / <c>test</c> / <c>prod</c>). The UI shows a warning banner
/// everywhere except <c>prod</c>, so an unset or unrecognised value maps to
/// <c>unknown</c> — a missing setting must never hide the banner on test data.
/// </summary>
public static class DeploymentEnvironment
{
    public const string ConfigKey = "Environment";

    public const string Dev = "dev";
    public const string Test = "test";
    public const string Prod = "prod";
    public const string Unknown = "unknown";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "dev" or "development" => Dev,
        "test" or "testing" => Test,
        "prod" or "production" => Prod,
        _ => Unknown,
    };
}
