using System.Globalization;
using Oaza.Application.Audit;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Interfaces;
using Oaza.Application.Ledger;
using Oaza.Application.UseCases;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Domain.Time;

namespace Oaza.Application.Seed;

/// <summary>
/// Import of the starting data from the CSV templates (T13). Every run is a dry run first: the files are imported into
/// an in-memory copy of the data through the same use cases the app uses (T02 validation, closings, audit), and the
/// report shows what would be created, what already exists unchanged, conflicts, errors and the house salda as of today.
/// <see cref="ApplyAsync"/> writes only when that dry run has no error and no conflict, so a second apply changes
/// nothing (rows are matched by natural keys) and an existing record is never overwritten.
/// </summary>
public class SeedImportUseCase
{
    public const string SeedImportEntity = "SeedImport";

    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    private static readonly Dictionary<string, MeterType> MeterTypes = new() { ["hlavni"] = MeterType.Main, ["hlavní"] = MeterType.Main, ["domovni"] = MeterType.Individual, ["domovní"] = MeterType.Individual };
    private static readonly Dictionary<string, AllocationBasis> Bases = new() { ["odecty"] = AllocationBasis.Metered, ["odečty"] = AllocationBasis.Metered, ["naklady"] = AllocationBasis.CostEntries, ["náklady"] = AllocationBasis.CostEntries };
    private static readonly Dictionary<string, WaterRole> WaterRoles = new() { ["spotreba"] = WaterRole.Consumption, ["spotřeba"] = WaterRole.Consumption, ["ztraty"] = WaterRole.Losses, ["ztráty"] = WaterRole.Losses };
    private static readonly Dictionary<string, AllocationMethod> Methods = new() { ["odecty"] = AllocationMethod.Metered, ["odečty"] = AllocationMethod.Metered, ["rovne"] = AllocationMethod.Equal, ["rovně"] = AllocationMethod.Equal, ["pomer"] = AllocationMethod.Ratio, ["poměr"] = AllocationMethod.Ratio, ["procenta"] = AllocationMethod.Percent };
    private static readonly Dictionary<string, CostEntryType> EntryTypes = new() { ["zaloha"] = CostEntryType.Advance, ["záloha"] = CostEntryType.Advance, ["vyuctovani"] = CostEntryType.Settlement, ["vyúčtování"] = CostEntryType.Settlement, ["jednorazovy"] = CostEntryType.OneOff, ["jednorázový"] = CostEntryType.OneOff };
    private static readonly Dictionary<string, PaidFrom> PaidFroms = new() { ["banka"] = PaidFrom.Bank, ["kredit"] = PaidFrom.SupplierCredit, ["pokladna"] = PaidFrom.Cash, ["jine"] = PaidFrom.Other, ["jiné"] = PaidFrom.Other };

    private readonly Stores _real;
    private readonly IClosingBoundary _closingBoundary;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public SeedImportUseCase(
        IHouseRepository houses,
        IOwnershipPeriodRepository periods,
        IWaterMeterRepository meters,
        IMeterReadingRepository readings,
        ICostComponentRepository components,
        IComponentAllocationRuleRepository rules,
        IParticipationRepository participations,
        IOpeningBalanceRepository balances,
        ICostEntryRepository entries,
        IAdvancePaymentRepository payments,
        IClosingBoundary closingBoundary,
        IAuditLogger audit,
        IClock clock)
    {
        _real = new Stores(houses, periods, meters, readings, components, rules, participations, balances, entries, payments);
        _closingBoundary = closingBoundary ?? throw new ArgumentNullException(nameof(closingBoundary));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Imports into a copy of the data and reports; writes nothing.</summary>
    public async Task<SeedImportReport> DryRunAsync(IReadOnlyDictionary<string, string> files)
    {
        var (report, _) = await RunDryAsync(files, new AuditActor("seed", "Import počátečních dat"));
        return report;
    }

    /// <summary>Dry run; when it has no error and no conflict, the same import against the real data.</summary>
    public async Task<SeedImportReport> ApplyAsync(IReadOnlyDictionary<string, string> files, AuditActor actor)
    {
        var (report, parsed) = await RunDryAsync(files, actor);
        if (!report.CanApply)
            return report;

        var real = new Run(this, _real, _audit, actor);
        await real.ImportAsync(parsed);
        report.Applied = true;
        report.Issues.AddRange(real.Issues);
        if (real.Issues.Count > 0)
            report.CanApply = false;
        await _audit.LogAsync(SeedImportEntity, $"seed-{_clock.Today:yyyyMMdd}", AuditActions.Create, null,
            report.Files.Select(f => new { f.File, f.Created }).ToList(), actor, "Import počátečních dat (T13)");
        return report;
    }

    private async Task<(SeedImportReport Report, Parsed Parsed)> RunDryAsync(IReadOnlyDictionary<string, string> files, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(files);
        var parsed = Parse(files);
        var copy = await Stores.CopyAsync(_real);
        var dry = new Run(this, copy, NullAudit.Instance, actor);
        await dry.ImportAsync(parsed);

        var report = new SeedImportReport
        {
            Today = _clock.Today,
            Files = SeedFiles.All.Select(spec => dry.Summary(spec.Name, parsed.Tables.ContainsKey(spec.Name))).ToList(),
            Issues = [.. parsed.Issues, .. dry.Issues],
        };
        report.CanApply = report.Issues.Count == 0 && report.Files.Any(f => f.Created > 0);

        var overview = await new HouseLedgerUseCase(
                new LedgerCostCollector(copy.Components, copy.Rules, copy.Participations, copy.Entries, copy.Balances, copy.Meters, copy.Readings, copy.Houses),
                copy.Houses, copy.Payments, copy.Balances, copy.Periods, copy.Components, _clock)
            .GetOverviewAsync(null, _clock.Today);
        report.From = overview.From;
        report.Houses = overview.Houses.Select(h => new SeedHouseSaldo(h.HouseName, h.Opening, h.Payments, h.Costs.Values.Sum(), h.Saldo)).ToList();
        report.Components = overview.Components.Select(c => new SeedComponentControl(c.ComponentName, c.AllocatedTotal, c.HousesTotal, c.Matches, c.Warnings)).ToList();
        return (report, parsed);
    }

    private static Parsed Parse(IReadOnlyDictionary<string, string> files)
    {
        var parsed = new Parsed();
        foreach (var (name, text) in files)
        {
            var file = Path.GetFileName(name ?? string.Empty).ToLowerInvariant();
            var spec = SeedFiles.All.FirstOrDefault(s => s.Name == file);
            if (spec is null)
            {
                parsed.Issues.Add(new SeedIssue(file, null, SeedIssue.Error, $"neznámý soubor — očekávané: {string.Join(", ", SeedFiles.All.Select(s => s.Name))}"));
                continue;
            }
            var (rows, errors) = SeedCsv.Parse(file, text, spec.Columns);
            parsed.Issues.AddRange(errors.Select(e => new SeedIssue(file, null, SeedIssue.Error, e)));
            if (errors.Count == 0)
                parsed.Tables[file] = rows;
        }
        return parsed;
    }

    private sealed class Parsed
    {
        public Dictionary<string, IReadOnlyList<SeedRow>> Tables { get; } = [];
        public List<SeedIssue> Issues { get; } = [];
    }

    /// <summary>The repositories one run works against — the real ones, or an in-memory copy for the dry run.</summary>
    private sealed record Stores(
        IHouseRepository Houses,
        IOwnershipPeriodRepository Periods,
        IWaterMeterRepository Meters,
        IMeterReadingRepository Readings,
        ICostComponentRepository Components,
        IComponentAllocationRuleRepository Rules,
        IParticipationRepository Participations,
        IOpeningBalanceRepository Balances,
        ICostEntryRepository Entries,
        IAdvancePaymentRepository Payments)
    {
        public static async Task<Stores> CopyAsync(Stores source)
        {
            async Task<TRepo> Copy<TRepo, T>(TRepo target, IRepository<T> from) where TRepo : MemoryRepo<T> where T : class
            {
                await target.LoadFromAsync(from);
                return target;
            }
            return new Stores(
                await Copy(new MemoryHouses { CopyOnAccess = true }, source.Houses),
                await Copy(new MemoryOwnershipPeriods { CopyOnAccess = true }, source.Periods),
                await Copy(new MemoryMeters { CopyOnAccess = true }, source.Meters),
                await Copy(new MemoryReadings { CopyOnAccess = true }, source.Readings),
                await Copy(new MemoryComponents { CopyOnAccess = true }, source.Components),
                await Copy(new MemoryRules { CopyOnAccess = true }, source.Rules),
                await Copy(new MemoryParticipations { CopyOnAccess = true }, source.Participations),
                await Copy(new MemoryOpeningBalances { CopyOnAccess = true }, source.Balances),
                await Copy(new MemoryCostEntries { CopyOnAccess = true }, source.Entries),
                await Copy(new MemoryPayments { CopyOnAccess = true }, source.Payments));
        }
    }

    private sealed class NullAudit : IAuditLogger
    {
        public static readonly NullAudit Instance = new();
        public Task LogAsync(string entityType, string entityId, string action, object? oldValue, object? newValue, AuditActor actor, string? reason = null) => Task.CompletedTask;
    }

    private enum Outcome { Created, Unchanged, Conflict, Error }

    /// <summary>An existing record with the same natural key but different values — never overwritten.</summary>
    private sealed class ConflictException(string message) : Exception(message);

    /// <summary>One import of all files against one set of stores.</summary>
    private sealed class Run
    {
        private readonly Stores _s;
        private readonly IAuditLogger _audit;
        private readonly AuditActor _actor;
        private readonly CostComponentsUseCase _componentsUseCase;
        private readonly OpeningBalancesUseCase _openingUseCase;
        private readonly CostEntriesUseCase _entriesUseCase;
        private readonly Dictionary<string, (int Rows, int Created, int Unchanged, int Conflicts, int Errors)> _counts = [];

        public Run(SeedImportUseCase owner, Stores stores, IAuditLogger audit, AuditActor actor)
        {
            _s = stores;
            _audit = audit;
            _actor = actor;
            _componentsUseCase = new CostComponentsUseCase(stores.Components, stores.Rules, stores.Participations, stores.Houses, owner._closingBoundary, audit, owner._clock);
            _openingUseCase = new OpeningBalancesUseCase(stores.Balances, stores.Periods, stores.Houses, stores.Meters, stores.Readings,
                stores.Components, stores.Rules, stores.Participations, owner._closingBoundary, audit, owner._clock);
            _entriesUseCase = new CostEntriesUseCase(stores.Entries, stores.Components, stores.Rules, stores.Participations, stores.Houses, owner._closingBoundary, audit);
        }

        public List<SeedIssue> Issues { get; } = [];

        public SeedFileSummary Summary(string file, bool uploaded)
        {
            var c = _counts.GetValueOrDefault(file);
            return new SeedFileSummary(file, uploaded, c.Rows, c.Created, c.Unchanged, c.Conflicts, c.Errors);
        }

        public async Task ImportAsync(Parsed parsed)
        {
            foreach (var spec in SeedFiles.All)
            {
                if (!parsed.Tables.TryGetValue(spec.Name, out var rows))
                    continue;
                if (spec.Name == SeedFiles.Participations)
                {
                    await ParticipationsAsync(rows);
                    continue;
                }
                foreach (var row in rows)
                    await RowAsync(row, () => spec.Name switch
                    {
                        SeedFiles.Houses => HouseAsync(row),
                        SeedFiles.OwnershipPeriods => PeriodAsync(row),
                        SeedFiles.Meters => MeterAsync(row),
                        SeedFiles.Components => ComponentAsync(row),
                        SeedFiles.AllocationRules => RuleAsync(row),
                        SeedFiles.OpeningMeterReadings => MeterReadingAsync(row),
                        SeedFiles.OpeningFundShares => FundShareAsync(row),
                        SeedFiles.ComponentCredits => CreditAsync(row),
                        SeedFiles.CostEntries => CostEntryAsync(row),
                        _ => throw new InvalidOperationException(spec.Name),
                    });
            }
        }

        private async Task RowAsync(SeedRow row, Func<Task<Outcome>> action) => Count(row.File, [row], await TryAsync([row], action));

        private async Task<Outcome> TryAsync(IReadOnlyList<SeedRow> rows, Func<Task<Outcome>> action)
        {
            try
            {
                return await action();
            }
            catch (ConflictException ex)
            {
                foreach (var row in rows)
                    Issues.Add(new SeedIssue(row.File, row.Line, SeedIssue.Conflict, ex.Message));
                return Outcome.Conflict;
            }
            catch (InvalidRowException)
            {
                foreach (var row in rows.Where(r => r.Errors.Count > 0))
                    Issues.Add(new SeedIssue(row.File, row.Line, SeedIssue.Error, string.Join("; ", row.Errors)));
                return Outcome.Error;
            }
            catch (AppException ex)
            {
                var message = ex is BusinessRuleException rule ? string.Join("; ", rule.Errors) : ex.Message;
                foreach (var row in rows)
                    Issues.Add(new SeedIssue(row.File, row.Line, SeedIssue.Error, message));
                return Outcome.Error;
            }
        }

        private void Count(string file, IReadOnlyList<SeedRow> rows, Outcome outcome)
        {
            var c = _counts.GetValueOrDefault(file);
            c.Rows += rows.Count;
            switch (outcome)
            {
                case Outcome.Created: c.Created += rows.Count; break;
                case Outcome.Unchanged: c.Unchanged += rows.Count; break;
                case Outcome.Conflict: c.Conflicts += rows.Count; break;
                default: c.Errors += rows.Count; break;
            }
            _counts[file] = c;
        }

        private sealed class InvalidRowException : Exception;

        private static void Check(SeedRow row)
        {
            if (row.Errors.Count > 0)
                throw new InvalidRowException();
        }

        private static void Same(string what, params (string Field, object? Existing, object? Imported)[] fields)
        {
            var differ = fields.Where(f => !Equals(Normalize(f.Existing), Normalize(f.Imported))).Select(f => $"{f.Field}: {Show(f.Existing)} → {Show(f.Imported)}").ToList();
            if (differ.Count > 0)
                throw new ConflictException($"{what} už existuje s jinými hodnotami ({string.Join(", ", differ)}) — upravte ho v aplikaci, import ho nepřepíše.");
        }

        private static object? Normalize(object? value) => value is string s ? (string.IsNullOrWhiteSpace(s) ? null : s.Trim()) : value;

        private static string Show(object? value) => value switch
        {
            null => "—",
            decimal d => d.ToString("#,##0.###", Czech).Replace('\u00A0', ' '),
            DateOnly day => day.ToString("d. M. yyyy", CultureInfo.InvariantCulture),
            bool b => b ? "ano" : "ne",
            _ => value.ToString() ?? "—",
        };

        // ───────────────────── Lookups by natural key ─────────────────────

        private async Task<House?> FindHouseAsync(string name) =>
            (await _s.Houses.GetByPartitionKeyAsync(PartitionKeys.House)).FirstOrDefault(h => string.Equals(h.Name.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase));

        private async Task<House> HouseByNameAsync(SeedRow row, string column)
        {
            var name = row.Required(column);
            Check(row);
            return await FindHouseAsync(name) ?? throw new AppException($"dům „{name}“ neexistuje (houses.csv nebo Správa domácností)");
        }

        private async Task<WaterMeter?> FindMeterAsync(string number) =>
            (await _s.Meters.GetByPartitionKeyAsync(PartitionKeys.Meter)).FirstOrDefault(m => string.Equals(m.MeterNumber.Trim(), number.Trim(), StringComparison.OrdinalIgnoreCase));

        private async Task<CostComponent?> FindComponentAsync(string code) =>
            (await _s.Components.GetAllComponentsAsync()).FirstOrDefault(c => string.Equals(c.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

        private async Task<CostComponent> ComponentByCodeAsync(SeedRow row)
        {
            var code = row.Required("component");
            Check(row);
            return await FindComponentAsync(code) ?? throw new AppException($"složka s kódem „{code}“ neexistuje (components.csv)");
        }

        // ───────────────────── Files ─────────────────────

        private async Task<Outcome> HouseAsync(SeedRow row)
        {
            var name = row.Required("name");
            var address = row.Text("address") ?? string.Empty;
            var contact = row.Text("contact_person") ?? string.Empty;
            var email = row.Text("email") ?? string.Empty;
            var active = row.Flag("active", true);
            Check(row);

            var existing = await FindHouseAsync(name);
            if (existing is not null)
            {
                Same($"Dům „{name}“", ("adresa", existing.Address, address), ("kontakt", existing.ContactPerson, contact), ("e-mail", existing.Email, email), ("aktivní", existing.IsActive, active));
                return Outcome.Unchanged;
            }
            var house = new House { Id = Guid.NewGuid().ToString(), Name = name, Address = address, ContactPerson = contact, Email = email, IsActive = active };
            await _s.Houses.UpsertAsync(house);
            await _audit.LogAsync("House", house.Id, AuditActions.Create, null, house, _actor, "Import počátečních dat");
            return Outcome.Created;
        }

        private async Task<Outcome> PeriodAsync(SeedRow row)
        {
            var owner = row.Required("owner_name");
            var contact = row.Text("contact");
            var from = row.Day("valid_from");
            var to = row.Day("valid_to", required: false);
            var house = await HouseByNameAsync(row, "house");
            Check(row);

            var periods = await _s.Periods.GetByHouseAsync(house.Id);
            var existing = periods.FirstOrDefault(p => p.ValidFrom == from);
            if (existing is not null)
            {
                Same($"Období vlastnictví {house.Name} od {Show(from)}", ("vlastník", existing.OwnerName, owner), ("kontakt", existing.Contact, contact), ("do", existing.ValidTo, to));
                return Outcome.Unchanged;
            }
            if (to < from)
                throw new AppException("konec období je před jeho začátkem");
            var overlap = periods.FirstOrDefault(p => p.ValidFrom <= (to ?? DateOnly.MaxValue) && from <= (p.ValidTo ?? DateOnly.MaxValue));
            if (overlap is not null)
                throw new AppException($"překrývá se s obdobím vlastníka {overlap.OwnerName} od {Show(overlap.ValidFrom)}");

            var period = new OwnershipPeriod { Id = OwnershipPeriod.KeyFor(house.Id, from!.Value), HouseId = house.Id, OwnerName = owner, Contact = contact, ValidFrom = from.Value, ValidTo = to };
            await _s.Periods.UpsertAsync(period);
            await _audit.LogAsync(OpeningBalancesUseCase.OwnershipPeriodEntity, period.Id, AuditActions.Create, null, period, _actor, "Import počátečních dat");
            return Outcome.Created;
        }

        private async Task<Outcome> MeterAsync(SeedRow row)
        {
            var number = row.Required("meter_number");
            var name = row.Text("name") ?? string.Empty;
            var type = row.Choice("type", MeterTypes);
            var installed = row.Day("installation_date", required: false);
            var radio = row.Text("radio_address");
            var houseName = row.Text("house");
            Check(row);
            House? house = null;
            if (type == MeterType.Individual)
                house = await HouseByNameAsync(row, "house");
            else if (houseName is not null)
                throw new AppException("hlavní vodoměr nepatří žádnému domu — nechte sloupec house prázdný");

            var existing = await FindMeterAsync(number);
            if (existing is not null)
            {
                Same($"Vodoměr {number}", ("název", existing.Name, name), ("typ", existing.Type, type), ("dům", existing.HouseId, house?.Id), ("rádiová adresa", existing.RadioAddress, radio));
                return Outcome.Unchanged;
            }
            if (type == MeterType.Main && (await _s.Meters.GetByPartitionKeyAsync(PartitionKeys.Meter)).Any(m => m.Type == MeterType.Main))
                throw new AppException("hlavní vodoměr už existuje");
            var meter = new WaterMeter
            {
                Id = Guid.NewGuid().ToString(), MeterNumber = number, Name = name, Type = type!.Value, HouseId = house?.Id, RadioAddress = radio,
                InstallationDate = (installed ?? default).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            };
            await _s.Meters.UpsertAsync(meter);
            await _audit.LogAsync("WaterMeter", meter.Id, AuditActions.Create, null, meter, _actor, "Import počátečních dat");
            return Outcome.Created;
        }

        private async Task<Outcome> ComponentAsync(SeedRow row)
        {
            var code = row.Required("code").ToUpperInvariant();
            var name = row.Required("name");
            var start = row.Day("start_date");
            var basis = row.Choice("allocation_basis", Bases);
            var role = row.Choice("water_role", WaterRoles, required: false) ?? WaterRole.None;
            var method = row.Choice("method", Methods, required: false);
            var ratioSource = row.Text("ratio_source")?.ToUpperInvariant();
            var note = row.Text("note");
            Check(row);

            var existing = await FindComponentAsync(code);
            if (existing is not null)
            {
                var first = (await _s.Rules.GetByComponentAsync(existing.Id)).OrderBy(r => r.ValidFrom).FirstOrDefault();
                Same($"Složka {code}", ("název", existing.Name, name), ("začátek", existing.StartDate, start), ("základ", existing.AllocationBasis, basis),
                    ("role vody", existing.WaterRole, role), ("metoda", first?.Method, method ?? first?.Method), ("poměr podle", first?.RatioSource, ratioSource));
                return Outcome.Unchanged;
            }
            await _componentsUseCase.CreateAsync(new CreateCostComponentRequest
            {
                Code = code, Name = name, StartDate = start!.Value, AllocationBasis = basis!.Value, WaterRole = role, Method = method, RatioSource = ratioSource, Note = note,
            }, _actor);
            return Outcome.Created;
        }

        private async Task<Outcome> RuleAsync(SeedRow row)
        {
            var from = row.Day("valid_from");
            var method = row.Choice("method", Methods);
            var ratioSource = row.Text("ratio_source")?.ToUpperInvariant();
            var reason = row.Required("reason");
            var component = await ComponentByCodeAsync(row);
            Check(row);

            var existing = (await _s.Rules.GetByComponentAsync(component.Id)).FirstOrDefault(r => r.ValidFrom == from);
            if (existing is not null)
            {
                Same($"Pravidlo {component.Code} od {Show(from)}", ("metoda", existing.Method, method), ("poměr podle", existing.RatioSource, ratioSource));
                return Outcome.Unchanged;
            }
            await _componentsUseCase.AddRuleAsync(component.Id, new AddAllocationRuleRequest { ValidFrom = from!.Value, Method = method!.Value, RatioSource = ratioSource, Reason = reason }, _actor);
            return Outcome.Created;
        }

        /// <summary>New participations of one component are added together (PERCENT = 100 % only for the whole set).</summary>
        private async Task ParticipationsAsync(IReadOnlyList<SeedRow> rows)
        {
            var pending = new List<(SeedRow Row, CostComponent Component, AddParticipationRequest Request)>();
            foreach (var row in rows)
            {
                AddParticipationRequest? request = null;
                CostComponent? component = null;
                var outcome = await TryAsync([row], async () =>
                {
                    var from = row.Day("valid_from");
                    var to = row.Day("valid_to", required: false);
                    var weight = row.Number("weight", required: false);
                    component = await ComponentByCodeAsync(row);
                    var house = await HouseByNameAsync(row, "house");
                    Check(row);
                    var existing = (await _s.Participations.GetByComponentAsync(component.Id)).FirstOrDefault(p => p.HouseId == house.Id && p.ValidFrom == from);
                    if (existing is not null)
                    {
                        Same($"Účast {house.Name} ve složce {component.Code} od {Show(from)}", ("do", existing.ValidTo, to), ("váha", existing.Weight, weight));
                        return Outcome.Unchanged;
                    }
                    request = new AddParticipationRequest { HouseId = house.Id, ValidFrom = from!.Value, ValidTo = to, Weight = weight, Reason = row.Text("reason") ?? "Import počátečních dat" };
                    return Outcome.Created;
                });
                if (outcome == Outcome.Created)
                    pending.Add((row, component!, request!));
                else
                    Count(row.File, [row], outcome);
            }

            foreach (var group in pending.GroupBy(p => p.Component.Id))
            {
                var groupRows = group.Select(p => p.Row).ToList();
                var outcome = await TryAsync(groupRows, async () =>
                {
                    await _componentsUseCase.AddParticipationsAsync(group.Key, group.Select(p => p.Request).ToList(), _actor);
                    return Outcome.Created;
                });
                Count(SeedFiles.Participations, groupRows, outcome);
            }
        }

        private async Task<Outcome> MeterReadingAsync(SeedRow row)
        {
            var number = row.Required("meter_number");
            var date = row.Day("date");
            var value = row.Number("value");
            var estimate = row.Flag("is_estimate", false);
            var source = row.Required("source");
            Check(row);
            var meter = await FindMeterAsync(number) ?? throw new AppException($"vodoměr „{number}“ neexistuje (meters.csv)");

            var existing = (await _s.Balances.GetAllBalancesAsync()).FirstOrDefault(b => b.Type == OpeningBalanceType.MeterReading && b.MeterId == meter.Id && b.Date == date);
            if (existing is not null)
            {
                Same($"Počáteční stav vodoměru {number} k {Show(date)}", ("stav", existing.Value, value), ("odhad", existing.IsEstimate, estimate));
                return Outcome.Unchanged;
            }
            await _openingUseCase.CreateAsync(new SaveOpeningBalanceRequest
            {
                Type = OpeningBalanceType.MeterReading, MeterId = meter.Id, Date = date!.Value, Value = value!.Value, IsEstimate = estimate, Source = source, Note = row.Text("note"),
            }, _actor);
            return Outcome.Created;
        }

        private async Task<Outcome> FundShareAsync(SeedRow row)
        {
            var date = row.Day("date");
            var value = row.Number("value");
            var source = row.Required("source");
            var house = await HouseByNameAsync(row, "house");
            Check(row);

            var existing = (await _s.Balances.GetAllBalancesAsync()).FirstOrDefault(b => b.Type == OpeningBalanceType.FundShare && b.HouseId == house.Id && b.Date == date);
            if (existing is not null)
            {
                Same($"Podíl na fondu {house.Name} k {Show(date)}", ("částka", existing.Value, value));
                return Outcome.Unchanged;
            }
            await _openingUseCase.CreateAsync(new SaveOpeningBalanceRequest
            {
                Type = OpeningBalanceType.FundShare, HouseId = house.Id, Date = date!.Value, Value = value!.Value, Source = source, Note = row.Text("note"),
            }, _actor);
            return Outcome.Created;
        }

        private async Task<Outcome> CreditAsync(SeedRow row)
        {
            var date = row.Day("date");
            var value = row.Number("value");
            var source = row.Required("source");
            var component = await ComponentByCodeAsync(row);
            Check(row);

            var existing = (await _s.Balances.GetAllBalancesAsync()).FirstOrDefault(b => b.Type == OpeningBalanceType.ComponentCredit && b.ComponentId == component.Id);
            if (existing is not null)
            {
                Same($"Kredit složky {component.Code}", ("datum", existing.Date, date), ("částka", existing.Value, value));
                return Outcome.Unchanged;
            }
            await _openingUseCase.CreateAsync(new SaveOpeningBalanceRequest
            {
                Type = OpeningBalanceType.ComponentCredit, ComponentId = component.Id, Date = date!.Value, Value = value!.Value, Source = source, Note = row.Text("note"),
            }, _actor);
            return Outcome.Created;
        }

        private async Task<Outcome> CostEntryAsync(SeedRow row)
        {
            var reference = row.Required("ref");
            var type = row.Choice("type", EntryTypes);
            var from = row.Day("period_from");
            var to = row.Day("period_to");
            var amount = row.Number("amount");
            var quantity = row.Number("quantity_m3", required: false);
            var paidFrom = row.Choice("paid_from", PaidFroms, required: false) ?? PaidFrom.Bank;
            var component = await ComponentByCodeAsync(row);
            Check(row);

            var existing = (await _s.Entries.GetAllAsync()).FirstOrDefault(e => e.ExternalRef == reference);
            if (existing is not null)
            {
                var existingComponent = (await _s.Components.GetAsync(PartitionKeys.CostComponent, existing.ComponentId))?.Code;
                Same($"Náklad {reference}", ("složka", existingComponent, component.Code), ("typ", existing.Type, type), ("od", existing.PeriodFrom, from), ("do", existing.PeriodTo, to),
                    ("částka", existing.Amount, amount), ("m³", existing.QuantityM3, quantity), ("placeno z", existing.PaidFrom, paidFrom));
                return Outcome.Unchanged;
            }
            await _entriesUseCase.CreateAsync(component.Id, new SaveCostEntryRequest
            {
                ExternalRef = reference, Type = type!.Value, PeriodFrom = from!.Value, PeriodTo = to!.Value, Amount = amount!.Value, QuantityM3 = quantity,
                Supplier = row.Text("supplier"), PaidFrom = paidFrom, Note = row.Text("note"),
            }, _actor);
            return Outcome.Created;
        }
    }
}
