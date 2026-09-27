using FluentAssertions;
using Oaza.Application.Audit;
using Oaza.Application.Seed;
using Oaza.Application.Tests.TestSupport;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Time;

namespace Oaza.Application.Tests.Seed;

public class SeedImportUseCaseTests
{
    private static readonly AuditActor Admin = new("u-admin", "Admin");

    private readonly MemoryHouses _houses = new();
    private readonly MemoryOwnershipPeriods _periods = new();
    private readonly MemoryMeters _meters = new();
    private readonly MemoryReadings _readings = new();
    private readonly MemoryComponents _components = new();
    private readonly MemoryRules _rules = new();
    private readonly MemoryParticipations _participations = new();
    private readonly MemoryOpeningBalances _balances = new();
    private readonly MemoryCostEntries _entries = new();
    private readonly MemoryPayments _payments = new();
    private readonly CountingAudit _audit = new();

    /// <summary>Today is 31. 12. 2026 in Prague — the S3 settlement for the second half of 2026 is in.</summary>
    private SeedImportUseCase Sut(DateOnly? closedUntil = null) => new(
        _houses, _periods, _meters, _readings, _components, _rules, _participations, _balances, _entries, _payments,
        new ClosedUntil(closedUntil), _audit, new PragueClock(new FixedTimeProvider(new DateTimeOffset(2026, 12, 31, 12, 0, 0, TimeSpan.Zero))));

    private static Dictionary<string, string> Samples() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "SeedSamples"), "*.csv")
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText);

    private int Stored() =>
        _houses.Items.Count + _periods.Items.Count + _meters.Items.Count + _readings.Items.Count + _components.Items.Count + _rules.Items.Count
        + _participations.Items.Count + _balances.Items.Count + _entries.Items.Count;

    [Fact]
    public async Task DryRun_OnTheSamples_ReportsTheGoldenSalda_AndWritesNothing()
    {
        var report = await Sut().DryRunAsync(Samples());

        report.Issues.Should().BeEmpty();
        report.CanApply.Should().BeTrue();
        report.Applied.Should().BeFalse();
        report.From.Should().Be(new DateOnly(2023, 11, 1));
        report.Today.Should().Be(new DateOnly(2026, 12, 31));
        report.Files.Single(f => f.File == SeedFiles.Participations).Created.Should().Be(17);
        report.Files.Single(f => f.File == SeedFiles.AllocationRules).Should().Be(new SeedFileSummary(SeedFiles.AllocationRules, true, 0, 0, 0, 0, 0));

        // S2: credit −20 000 over A–D = +5 000 each, two advances of 500 from the credit = −250; S3: A–D 414, E 184.
        report.Houses.Select(h => (h.HouseName, h.Saldo)).Should().Equal(
            ("RD A", 5_336m), ("RD B", 4_336m), ("RD C", 4_336m), ("RD D", 4_336m), ("RD E", -184m), ("RD F", 0m));
        report.Houses[0].Opening.Should().Be(1_000m);
        report.Components.Should().Contain(c => c.ComponentName == "Osvětlení" && c.Allocated == 1_840m && c.Matches);

        Stored().Should().Be(0);
        _audit.Count.Should().Be(0);
    }

    [Fact]
    public async Task Apply_WritesEverythingOnce_AndASecondApplyChangesNothing()
    {
        var first = await Sut().ApplyAsync(Samples(), Admin);

        first.Applied.Should().BeTrue();
        first.Issues.Should().BeEmpty();
        _houses.Items.Should().HaveCount(6);
        _meters.Items.Should().HaveCount(5);
        _readings.Items.Should().HaveCount(5); // the opening meter values are readings too (T03)
        _readings.Items.Values.Single(r => r.Value == 120.125m).IsEstimate.Should().BeTrue();
        _entries.Items.Values.Select(e => e.ExternalRef).Should().BeEquivalentTo(["S2-2023-11", "S2-2023-12", "S3-2026-H2"]);
        _rules.Items.Values.Single(r => r.RatioSource == "VODA_PVK").Method.Should().Be(AllocationMethod.Ratio);
        var stored = Stored();
        var audited = _audit.Count;
        audited.Should().BeGreaterThan(stored - 5); // every created record + the summary entry

        var second = await Sut().ApplyAsync(Samples(), Admin);

        second.Applied.Should().BeFalse();
        second.CanApply.Should().BeFalse();
        second.Issues.Should().BeEmpty();
        second.Files.Where(f => f.Rows > 0).Should().OnlyContain(f => f.Created == 0 && f.Unchanged == f.Rows);
        second.Houses.Select(h => h.Saldo).Should().Equal(first.Houses.Select(h => h.Saldo));
        Stored().Should().Be(stored);
        _audit.Count.Should().Be(audited);
    }

    [Fact]
    public async Task ChangedValueOfAnExistingRecord_IsAConflict_AndBlocksApply()
    {
        await Sut().ApplyAsync(Samples(), Admin);
        var files = Samples();
        files[SeedFiles.CostEntries] = files[SeedFiles.CostEntries].Replace("1 840,00", "1 900,00");
        files[SeedFiles.Houses] += "RD G;Zadní Kopanina 907;Gustav Nový;g@example.invalid;ano\n";

        var report = await Sut().ApplyAsync(files, Admin);

        report.Applied.Should().BeFalse();
        report.Issues.Should().ContainSingle().Which.Should().Match<SeedIssue>(i =>
            i.File == SeedFiles.CostEntries && i.Line == 4 && i.Severity == SeedIssue.Conflict && i.Message.Contains("1 840 → 1 900"));
        report.Files.Single(f => f.File == SeedFiles.Houses).Created.Should().Be(1);
        _houses.Items.Should().HaveCount(6); // nothing written
    }

    [Fact]
    public async Task InvalidRows_AreReportedWithFileAndLine()
    {
        var files = new Dictionary<string, string>
        {
            [SeedFiles.Houses] = "name;address;contact_person;email;active\nRD A;;;;možná\n",
            [SeedFiles.Meters] = "meter_number;name;type;house;installation_date;radio_address\nV-1;;domovni;RD X;;\nH-1;;hlavni;RD A;;\n",
            [SeedFiles.Components] = "code;name;start_date;allocation_basis;water_role;method;ratio_source;note\nX;;1. 13. 2023;nic;;;;\n",
            [SeedFiles.CostEntries] = "ref;component\n",
            ["pozemky.csv"] = "a;b\n",
        };

        var report = await Sut().DryRunAsync(files);

        report.CanApply.Should().BeFalse();
        report.Issues.Select(i => (i.File, i.Line)).Should().BeEquivalentTo(new (string, int?)[]
        {
            ("pozemky.csv", null), (SeedFiles.CostEntries, null), (SeedFiles.Houses, 2), (SeedFiles.Meters, 2), (SeedFiles.Meters, 3), (SeedFiles.Components, 2),
        });
        report.Issues.Single(i => i.File == SeedFiles.Houses).Message.Should().Contain("ano, nebo ne");
        report.Issues.Single(i => i.Line == 2 && i.File == SeedFiles.Meters).Message.Should().Contain("dům „RD X“ neexistuje");
        report.Issues.Single(i => i.File == SeedFiles.Components).Message.Should().Contain("není datum").And.Contain("není povolená hodnota");
    }

    [Fact]
    public async Task PercentParticipationsOfAComponent_AreValidatedTogether()
    {
        var files = Samples();
        files[SeedFiles.Components] += "UDRZBA;Údržba;2023-11-01;naklady;;procenta;;\n";
        files[SeedFiles.Participations] += "UDRZBA;RD A;2023-11-01;;60;\nUDRZBA;RD B;2023-11-01;;40;\n";

        var ok = await Sut().DryRunAsync(files);
        files[SeedFiles.Participations] = files[SeedFiles.Participations].Replace("UDRZBA;RD B;2023-11-01;;40;", "UDRZBA;RD B;2023-11-01;;30;");
        var wrong = await Sut().DryRunAsync(files);

        ok.Issues.Should().BeEmpty();
        wrong.Issues.Should().HaveCount(2).And.OnlyContain(i => i.File == SeedFiles.Participations && i.Message.Contains("100"));
        wrong.Files.Single(f => f.File == SeedFiles.Participations).Errors.Should().Be(2);
    }

    [Fact]
    public async Task EntryInAClosedPeriod_IsAnError_BecauseTheImportGivesNoReason()
    {
        var report = await Sut(closedUntil: new DateOnly(2024, 12, 31)).DryRunAsync(Samples());

        report.Issues.Should().Contain(i => i.File == SeedFiles.CostEntries && i.Line == 2 && i.Message.Contains("uzavřeného období"));
        report.CanApply.Should().BeFalse();
    }

    [Fact]
    public async Task AllocationRuleChange_IsImportedOnce()
    {
        var files = Samples();
        files[SeedFiles.AllocationRules] += "ZTRATY_VODY;2025-01-01;rovne;;hlasování schůze 12/2024\n";

        (await Sut().ApplyAsync(files, Admin)).Issues.Should().BeEmpty();
        var again = await Sut().DryRunAsync(files);

        _rules.Items.Values.Where(r => r.ComponentId == _components.Items.Values.Single(c => c.Code == "ZTRATY_VODY").Id)
            .Select(r => (r.ValidFrom, r.ValidTo, r.Method)).Should().BeEquivalentTo(new[]
            {
                (new DateOnly(2023, 11, 1), (DateOnly?)new DateOnly(2024, 12, 31), AllocationMethod.Ratio),
                (new DateOnly(2025, 1, 1), (DateOnly?)null, AllocationMethod.Equal),
            });
        again.Files.Single(f => f.File == SeedFiles.AllocationRules).Unchanged.Should().Be(1);
        again.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task OwnershipAndMeterRules_AreChecked()
    {
        var files = Samples();
        files[SeedFiles.OwnershipPeriods] += "RD A;Někdo jiný;;2024-01-01;\nRD B;Nový;;2025-05-01;2025-04-01\n";
        files[SeedFiles.Meters] += "H-999;Druhý hlavní;hlavni;;;\n";

        var report = await Sut().DryRunAsync(files);

        report.Issues.Select(i => (i.File, i.Line, i.Message)).Should().BeEquivalentTo(new (string, int?, string)[]
        {
            (SeedFiles.OwnershipPeriods, 8, "překrývá se s obdobím vlastníka Alena Ukázková od 1. 11. 2023"),
            (SeedFiles.OwnershipPeriods, 9, "konec období je před jeho začátkem"),
            (SeedFiles.Meters, 7, "hlavní vodoměr už existuje"),
        });
    }

    [Fact]
    public void Templates_HaveExactlyTheImportedColumns()
    {
        foreach (var spec in SeedFiles.All)
        {
            var header = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "SeedTemplates", spec.Name))[0];
            header.Split(';').Should().Equal(spec.Columns, spec.Name);
        }
    }

    private sealed class CountingAudit : IAuditLogger
    {
        public int Count { get; private set; }

        public Task LogAsync(string entityType, string entityId, string action, object? oldValue, object? newValue, AuditActor actor, string? reason = null)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}
