namespace Oaza.Application.Deployment;

/// <summary>
/// Guard that an environment uses its own storage account (T01, R11): test and prod must never share data. Accounts
/// follow the naming of <c>docs/DEPLOYMENT-TEST-PROD.md</c> — <c>stoazadev</c>, <c>stoazatest</c>, <c>stoaza</c> —
/// so prod refuses an account ending in <c>dev</c>/<c>test</c> or the local emulator, and dev/test refuse any account
/// that is not theirs (e.g. the prod one). The Functions host does not start on a violation.
/// </summary>
public static class StorageIsolation
{
    /// <summary>The local storage emulator (Azurite) account.</summary>
    public const string EmulatorAccount = "devstoreaccount1";

    /// <returns>Why the account must not be used in the environment, or null when it is fine.</returns>
    public static string? Violation(string environment, string accountName)
    {
        var account = (accountName ?? string.Empty).Trim().ToLowerInvariant();
        return DeploymentEnvironment.Normalize(environment) switch
        {
            DeploymentEnvironment.Prod when account == EmulatorAccount || account.EndsWith(DeploymentEnvironment.Dev, StringComparison.Ordinal) || account.EndsWith(DeploymentEnvironment.Test, StringComparison.Ordinal) =>
                $"Produkce nesmí používat vývojový ani testovací storage ({accountName}).",
            DeploymentEnvironment.Test when account != EmulatorAccount && !account.EndsWith(DeploymentEnvironment.Test, StringComparison.Ordinal) =>
                $"Testovací prostředí smí používat jen testovací storage (…test) nebo emulátor, ne {accountName}.",
            DeploymentEnvironment.Dev when account != EmulatorAccount && !account.EndsWith(DeploymentEnvironment.Dev, StringComparison.Ordinal) =>
                $"Vývojové prostředí smí používat jen vývojový storage (…dev) nebo emulátor, ne {accountName}.",
            _ => null,
        };
    }
}
