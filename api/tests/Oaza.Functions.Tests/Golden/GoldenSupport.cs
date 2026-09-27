using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Oaza.Application.Audit;
using Oaza.Application.Interfaces;
using Oaza.Application.Ledger;
using Oaza.Application.OffBookFunds;
using Oaza.Application.Seed;
using Oaza.Application.UseCases;
using Oaza.Application.Deployment;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;
using Oaza.Domain.Time;
using Oaza.Functions.Endpoints;

namespace Oaza.Functions.Tests.Golden;

// Test doubles copied from Oaza.Application.Tests/TestSupport (a test project must not reference another one).

/// <summary>A clock that starts at a fixed instant and moves one second per read: deterministic, yet
/// records created in one scenario get distinct <c>CreatedAt</c> (the day never changes).</summary>
public sealed class SteppingTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        var now = _now;
        _now = _now.AddSeconds(1);
        return now;
    }
}

public sealed class NoAuditLogger : IAuditLogger
{
    public Task LogAsync(string entityType, string entityId, string action, object? oldValue, object? newValue, AuditActor actor, string? reason = null) =>
        Task.CompletedTask;
}

public sealed class MemoryInterimClosings() : MemoryRepo<InterimClosing>(_ => PartitionKeys.InterimClosing, c => c.Id), IInterimClosingRepository
{
    public Task<IReadOnlyList<InterimClosing>> GetAllClosingsAsync() => GetByPartitionKeyAsync(PartitionKeys.InterimClosing);
}

public sealed class MemoryCashBook() : MemoryRepo<CashBookEntry>(_ => PartitionKeys.CashBook, e => e.Id), ICashBookRepository
{
    public Task<IReadOnlyList<CashBookEntry>> GetAllEntriesAsync() => GetByPartitionKeyAsync(PartitionKeys.CashBook);
}

public sealed class MemoryOffBookFunds : IOffBookFundRepository
{
    private readonly List<OffBookFund> _funds = [];
    private readonly List<FundRecord> _records = [];

    public Task<IReadOnlyList<OffBookFund>> GetFundsAsync() => Task.FromResult<IReadOnlyList<OffBookFund>>(_funds.ToList());
    public Task<OffBookFund?> GetFundAsync(string fundId) => Task.FromResult(_funds.FirstOrDefault(f => f.Id == fundId));

    public Task UpsertFundAsync(OffBookFund fund)
    {
        _funds.RemoveAll(f => f.Id == fund.Id);
        _funds.Add(fund);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FundRecord>> GetRecordsAsync(string fundId) =>
        Task.FromResult<IReadOnlyList<FundRecord>>(_records.Where(r => r.FundId == fundId).ToList());

    public Task<FundRecord?> GetRecordAsync(string fundId, string recordId) =>
        Task.FromResult(_records.FirstOrDefault(r => r.FundId == fundId && r.Id == recordId));

    public Task UpsertRecordAsync(FundRecord record)
    {
        var index = _records.FindIndex(r => r.FundId == record.FundId && r.Id == record.Id);
        if (index >= 0)
            _records[index] = record;
        else
            _records.Add(record);
        return Task.CompletedTask;
    }

    public Task DeleteRecordAsync(string fundId, string recordId)
    {
        _records.RemoveAll(r => r.FundId == fundId && r.Id == recordId);
        return Task.CompletedTask;
    }
}

/// <summary>All repositories of one scenario (in memory) and the real use cases over them, wired like <c>Program.cs</c>.</summary>
public sealed class GoldenWorld(DateTimeOffset now)
{
    public static readonly AuditActor Actor = new("u-admin", "Admin Test");
    public static readonly LedgerRequester Admin = new(Domain.Enums.UserRole.Admin, null);

    public readonly MemoryComponents Components = new();
    public readonly MemoryRules Rules = new();
    public readonly MemoryParticipations Participations = new();
    public readonly MemoryCostEntries Entries = new();
    public readonly MemoryOpeningBalances Openings = new();
    public readonly MemoryMeters Meters = new();
    public readonly MemoryReadings Readings = new();
    public readonly MemoryHouses Houses = new();
    public readonly MemoryPayments Payments = new();
    public readonly MemoryOwnershipPeriods Periods = new();
    public readonly MemoryInterimClosings Closings = new();
    public readonly MemoryCashBook CashBook = new();
    public readonly MemoryOffBookFunds Funds = new();
    public readonly IAuditLogger Audit = new NoAuditLogger();
    public readonly PragueClock Clock = new(new SteppingTimeProvider(now));

    public IClosingBoundary Boundary => new InterimClosingBoundary(Closings);

    public WaterSettlementUseCase Water() => new(Components, Rules, Participations, Entries, Meters, Readings, Houses);

    public HouseLedgerUseCase Ledger() => new(
        new LedgerCostCollector(Components, Rules, Participations, Entries, Openings, Meters, Readings, Houses),
        Houses, Payments, Openings, Periods, Components, Clock);

    public CostComponentsUseCase CostComponents() => new(Components, Rules, Participations, Houses, Boundary, Audit, Clock);

    public CostEntriesUseCase CostEntries() => new(Entries, Components, Rules, Participations, Houses, Boundary, Audit);

    public OpeningBalancesUseCase OpeningBalances() =>
        new(Openings, Periods, Houses, Meters, Readings, Components, Rules, Participations, Boundary, Audit, Clock);

    public InterimClosingsUseCase InterimClosings() => new(Closings, Houses, Ledger(), Audit, Clock);

    public HouseTransferUseCase HouseTransfer() =>
        new(Houses, Periods, Meters, Readings, Boundary, Ledger(), InterimClosings(), OpeningBalances(), Audit, Clock);

    public CashBookUseCase CashBookUseCase() => new(CashBook, Components, CostEntries(), Boundary, Audit, Clock);

    public OffBookFundUseCase OffBookFund() => new(Funds, Houses, new FeatureFlags(true), Audit, Clock);
}

/// <summary>
/// Golden fixture files in <c>web/e2e/golden/</c>: the JSON the API would send for a scenario, compared with the
/// committed file (or rewritten with <c>UPDATE_GOLDEN=1</c>). The Playwright specs serve these files as the mocked API.
/// </summary>
public static partial class Golden
{
    private const string Regenerate = "UPDATE_GOLDEN=1 dotnet test Oaza.sln --filter FullyQualifiedName~Golden (from api/), then commit web/e2e/golden/*.json";

    /// <summary>
    /// <see cref="ModelEndpoint.JsonOptions"/> (what the API sends: camelCase, string enums, yyyy-MM-dd days) plus
    /// indentation and unescaped Czech letters, so the committed files diff readably. Both change whitespace and
    /// escaping only — the parsed JSON is identical to the API response. Do not "fix" this back to compact.
    /// </summary>
    public static readonly JsonSerializerOptions FileOptions = new(ModelEndpoint.JsonOptions)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    /// <summary>The API's 400 body for a business rule (mirrors <c>ModelEndpoint.HandleAsync</c>).</summary>
    public static object BusinessRuleError(Oaza.Application.Exceptions.BusinessRuleException ex) =>
        new { error = ex.Message, errors = ex.Errors.Select(message => new { field = string.Empty, message }).ToList() };

    /// <summary>
    /// Serializes <paramref name="fixture"/>, replaces generated GUIDs by stable ids (<c>id-1</c>, <c>id-2</c>, … in order
    /// of first appearance, over the whole file so cross references stay consistent) and compares it with
    /// <c>web/e2e/golden/{name}.json</c>.
    /// </summary>
    public static void Verify(string name, object fixture)
    {
        var json = JsonSerializer.Serialize(fixture, FileOptions);
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        json = GuidPattern().Replace(json, m =>
        {
            if (!ids.TryGetValue(m.Value, out var id))
                ids[m.Value] = id = $"id-{ids.Count + 1}";
            return id;
        });
        json = json.ReplaceLineEndings("\n") + "\n";

        var path = Path.Combine(GoldenDirectory(), $"{name}.json");
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
            return;
        }

        if (!File.Exists(path))
            Assert.Fail($"Golden fixture {path} is missing. Generate it: {Regenerate}.");
        var committed = File.ReadAllText(path).ReplaceLineEndings("\n");
        if (committed != json)
            Assert.Fail($"Golden fixture {path} differs from the current backend output. If the change is intended, regenerate: {Regenerate}.\n--- expected (backend) ---\n{json}");
    }

    private static string GoldenDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "api", "Oaza.sln")) && Directory.Exists(Path.Combine(dir.FullName, "web", "e2e")))
                return Path.Combine(dir.FullName, "web", "e2e", "golden");
        }
        throw new InvalidOperationException("Repository root (api/Oaza.sln + web/e2e) not found above " + AppContext.BaseDirectory);
    }
}
