namespace Oaza.Application.Seed;

/// <summary>A seed CSV template: file name and its columns.</summary>
public sealed record SeedFileSpec(string Name, string[] Columns);

/// <summary>The seed templates (T13) in import order — every file may refer only to the files before it.</summary>
public static class SeedFiles
{
    public const string Houses = "houses.csv";
    public const string OwnershipPeriods = "ownership_periods.csv";
    public const string Meters = "meters.csv";
    public const string Components = "components.csv";
    public const string AllocationRules = "allocation_rules.csv";
    public const string Participations = "participations.csv";
    public const string OpeningMeterReadings = "opening_meter_readings.csv";
    public const string OpeningFundShares = "opening_fund_shares.csv";
    public const string ComponentCredits = "component_credits.csv";
    public const string CostEntries = "cost_entries.csv";

    public static readonly IReadOnlyList<SeedFileSpec> All =
    [
        new(Houses, ["name", "address", "contact_person", "email", "active"]),
        new(OwnershipPeriods, ["house", "owner_name", "contact", "valid_from", "valid_to"]),
        new(Meters, ["meter_number", "name", "type", "house", "installation_date", "radio_address"]),
        new(Components, ["code", "name", "start_date", "allocation_basis", "water_role", "method", "ratio_source", "note"]),
        new(AllocationRules, ["component", "valid_from", "method", "ratio_source", "reason"]),
        new(Participations, ["component", "house", "valid_from", "valid_to", "weight", "reason"]),
        new(OpeningMeterReadings, ["meter_number", "date", "value", "is_estimate", "source", "note"]),
        new(OpeningFundShares, ["house", "date", "value", "source", "note"]),
        new(ComponentCredits, ["component", "date", "value", "source", "note"]),
        new(CostEntries, ["ref", "component", "type", "period_from", "period_to", "amount", "quantity_m3", "supplier", "paid_from", "note"]),
    ];
}

/// <summary>A problem with one row (or a whole file when <see cref="Line"/> is null).</summary>
public sealed record SeedIssue(string File, int? Line, string Severity, string Message)
{
    public const string Error = "chyba";
    public const string Conflict = "konflikt";
}

/// <summary>What the import does with one file: rows created, already there unchanged, conflicts and errors.</summary>
public sealed record SeedFileSummary(string File, bool Uploaded, int Rows, int Created, int Unchanged, int Conflicts, int Errors);

/// <summary>A house's saldo as of today after the import (X1: positive = přeplatek); costs are positive.</summary>
public sealed record SeedHouseSaldo(string HouseName, decimal Opening, decimal Payments, decimal Costs, decimal Saldo);

/// <summary>A component's allocated total vs. the sum over houses (T07 control row).</summary>
public sealed record SeedComponentControl(string ComponentName, decimal Allocated, decimal Houses, bool Matches, IReadOnlyList<string> Warnings);

/// <summary>Result of the seed import (T13) — the dry-run report, or the apply result.</summary>
public sealed class SeedImportReport
{
    /// <summary>True when the data were written.</summary>
    public bool Applied { get; set; }

    /// <summary>No error, no conflict and something to create.</summary>
    public bool CanApply { get; set; }

    public DateOnly From { get; set; }
    public DateOnly Today { get; set; }
    public List<SeedFileSummary> Files { get; set; } = [];
    public List<SeedIssue> Issues { get; set; } = [];
    public List<SeedHouseSaldo> Houses { get; set; } = [];
    public List<SeedComponentControl> Components { get; set; } = [];
}
