using Oaza.Infrastructure.Backup;

// Backup / restore of a whole storage account to a folder (release checklist, T14).
// The connection string is read from an environment variable — never pass it on the command line.
//
//   dotnet run --project api/tools/Oaza.StorageBackup -- backup  <folder> [--connection-env OAZA_STORAGE_CONNECTION]
//   dotnet run --project api/tools/Oaza.StorageBackup -- restore <folder> --yes <account name> [--connection-env …]

if (args.Length < 2 || args[0] is not ("backup" or "restore"))
{
    Console.Error.WriteLine("Použití: backup <složka> | restore <složka> --yes <název účtu>  [--connection-env PROMĚNNÁ]");
    return 2;
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var variable = Option("--connection-env") ?? "OAZA_STORAGE_CONNECTION";
var connection = Environment.GetEnvironmentVariable(variable);
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine($"Nastavte connection string storage do proměnné prostředí {variable}.");
    return 2;
}
var account = new Azure.Storage.Blobs.BlobServiceClient(connection).AccountName;
var folder = Path.GetFullPath(args[1]);
var backup = new StorageBackup(connection);

if (args[0] == "backup")
{
    Console.WriteLine($"Záloha účtu {account} do {folder} …");
    Print(await backup.BackupAsync(folder));
    return 0;
}

if (Option("--yes") != account)
{
    Console.Error.WriteLine($"Obnova PŘEPÍŠE data účtu {account} podle zálohy {folder} (a smaže, co v záloze není).");
    Console.Error.WriteLine($"Pro potvrzení spusťte znovu s --yes {account}");
    return 3;
}
Console.WriteLine($"Obnova účtu {account} ze zálohy {folder} …");
Print(await backup.RestoreAsync(folder));
return 0;

static void Print(BackupSummary summary)
{
    foreach (var (name, count) in summary.Tables.OrderBy(t => t.Key, StringComparer.Ordinal))
        Console.WriteLine($"  tabulka {name}: {count}");
    foreach (var (name, count) in summary.Containers.OrderBy(t => t.Key, StringComparer.Ordinal))
        Console.WriteLine($"  kontejner {name}: {count}");
    if (summary.Deleted > 0)
        Console.WriteLine($"  smazáno navíc: {summary.Deleted}");
}
