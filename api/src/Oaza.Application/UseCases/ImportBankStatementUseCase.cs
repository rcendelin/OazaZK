using Microsoft.Extensions.Logging;
using Oaza.Application.BankImport;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Helpers;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.UseCases;

/// <summary>
/// Two-step import of a Fio CSV bank statement into household payments.
/// <see cref="PreviewAsync"/> parses the file and suggests, for every incoming
/// payment, the house (by counter-account), type (advance/doplatek), month and
/// component split — nothing is saved. <see cref="ConfirmAsync"/> takes the
/// admin's final decisions, re-validates them and writes the payments, the
/// processed-movement records (idempotence) and the learned house accounts.
/// </summary>
public class ImportBankStatementUseCase
{
    private const decimal Tolerance = 0.005m;

    private readonly IHouseRepository _houseRepository;
    private readonly IAdvancePaymentRepository _advanceRepository;
    private readonly IBankAccountMappingRepository _mappingRepository;
    private readonly IBankTransactionRepository _transactionRepository;
    private readonly CalculatePrescribedAdvancesUseCase _prescribedAdvancesUseCase;
    private readonly ILogger<ImportBankStatementUseCase> _logger;

    public ImportBankStatementUseCase(
        IHouseRepository houseRepository,
        IAdvancePaymentRepository advanceRepository,
        IBankAccountMappingRepository mappingRepository,
        IBankTransactionRepository transactionRepository,
        CalculatePrescribedAdvancesUseCase prescribedAdvancesUseCase,
        ILogger<ImportBankStatementUseCase> logger)
    {
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _advanceRepository = advanceRepository ?? throw new ArgumentNullException(nameof(advanceRepository));
        _mappingRepository = mappingRepository ?? throw new ArgumentNullException(nameof(mappingRepository));
        _transactionRepository = transactionRepository ?? throw new ArgumentNullException(nameof(transactionRepository));
        _prescribedAdvancesUseCase = prescribedAdvancesUseCase ?? throw new ArgumentNullException(nameof(prescribedAdvancesUseCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ───────────────────── Preview ─────────────────────

    public async Task<BankImportPreviewResponse> PreviewAsync(byte[] fileContent)
    {
        var statement = FioCsvParser.Parse(fileContent);
        var ownAccount = statement.OwnAccount!;

        var prescribed = await _prescribedAdvancesUseCase.CalculateAsync();
        var prescribedByHouse = prescribed.Houses.ToDictionary(h => h.HouseId, h => h.Actual);

        var houses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
        var activeHouseIds = houses.Where(h => h.IsActive).Select(h => h.Id).ToHashSet();

        var mappings = (await _mappingRepository.GetAllMappingsAsync())
            .ToDictionary(m => m.AccountKey, m => m.HouseId);

        var processed = (await _transactionRepository.GetByAccountAsync(ownAccount.ToKey()))
            .ToDictionary(t => t.TransactionId);

        var existingAdvanceMonths = new Dictionary<string, HashSet<string>>();
        foreach (var houseId in activeHouseIds)
        {
            existingAdvanceMonths[houseId] = (await _advanceRepository.GetByHouseIdAsync(houseId))
                .Where(p => p.Type == PaymentType.Advance)
                .Select(p => PaymentRowKeys.Advance(p.Year, p.Month))
                .ToHashSet();
        }

        // Advances suggested by earlier rows of this same statement (one per house per month).
        var claimedMonths = new HashSet<string>();

        var rows = new List<BankImportRow>();
        foreach (var tx in statement.Transactions)
        {
            var counterAccount = BankAccountNumber.FromParts(tx.CounterAccount, tx.CounterBankCode);
            var row = new BankImportRow
            {
                TransactionId = tx.Id,
                Date = tx.Date,
                Amount = tx.Amount,
                Currency = tx.Currency,
                CounterAccount = counterAccount?.ToString() ?? JoinAccount(tx.CounterAccount, tx.CounterBankCode),
                CounterName = tx.CounterName,
                Message = tx.Message,
                Note = JoinNotes(tx.Note, tx.Note2),
                VariableSymbol = tx.VariableSymbol,
                BankOperationType = tx.Type,
                Year = tx.Date.Year,
                Month = tx.Date.Month,
            };
            rows.Add(row);

            if (tx.Amount < 0)
            {
                row.Status = BankImportRowStatus.Outgoing;
                continue;
            }

            if (!string.Equals(tx.Currency, "CZK", StringComparison.OrdinalIgnoreCase))
            {
                row.Status = BankImportRowStatus.UnsupportedCurrency;
                row.Warnings.Add($"Platba v měně {tx.Currency} se neimportuje.");
                continue;
            }

            if (processed.TryGetValue(tx.Id, out var done))
            {
                row.Status = done.Status == BankTransactionStatus.Ignored
                    ? BankImportRowStatus.Ignored
                    : BankImportRowStatus.AlreadyImported;
                row.HouseId = done.HouseId;
                continue;
            }

            // House assignment: by counter-account only.
            if (counterAccount is not null && mappings.TryGetValue(counterAccount.ToKey(), out var mappedHouseId))
            {
                if (activeHouseIds.Contains(mappedHouseId))
                {
                    row.HouseId = mappedHouseId;
                    row.MatchSource = BankImportMatchSource.Account;
                }
                else
                {
                    row.Warnings.Add("Účet patří neaktivní domácnosti — vyberte domácnost ručně.");
                }
            }

            SuggestTypeAndSplit(row, tx.Message, prescribedByHouse, existingAdvanceMonths, claimedMonths);
        }

        return new BankImportPreviewResponse
        {
            Statement = new BankStatementSummary
            {
                Account = ownAccount.ToString(),
                Number = statement.StatementNumber,
                DateFrom = statement.DateFrom,
                DateTo = statement.DateTo,
                OpeningBalance = statement.OpeningBalance,
                ClosingBalance = statement.ClosingBalance,
                TotalIncome = statement.TotalIncome,
                TotalExpense = statement.TotalExpense,
                SumCheckOk = statement.Warnings.Count == 0,
            },
            Rows = rows,
            Warnings = statement.Warnings,
            Houses = prescribed.Houses
                .Select(h => new BankImportHouse
                {
                    HouseId = h.HouseId,
                    HouseName = h.HouseName,
                    WaterAmount = h.Actual.Water,
                    ElectricityAmount = h.Actual.Electricity,
                    CommonAmount = h.Actual.Common,
                    TotalAmount = h.Actual.Total,
                })
                .OrderBy(h => h.HouseName)
                .ToList(),
            ExistingAdvanceMonths = existingAdvanceMonths.ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(m => m).ToList()),
        };
    }

    /// <summary>
    /// Type: "doplat…" in the message → doplatek; the prescribed amount for a month
    /// without an advance yet → advance; anything else → doplatek with a warning.
    /// Split: the prescribed split for a matching advance, otherwise proportional.
    /// </summary>
    private static void SuggestTypeAndSplit(
        BankImportRow row,
        string? message,
        IReadOnlyDictionary<string, AdvanceSplit> prescribedByHouse,
        IReadOnlyDictionary<string, HashSet<string>> existingAdvanceMonths,
        HashSet<string> claimedMonths)
    {
        var prescribed = row.HouseId is not null ? prescribedByHouse.GetValueOrDefault(row.HouseId) : null;
        var monthKey = PaymentRowKeys.Advance(row.Year, row.Month);
        var saysDoplatek = message?.Contains("doplat", StringComparison.OrdinalIgnoreCase) == true;

        if (row.HouseId is not null && !saysDoplatek && prescribed is not null && prescribed.Total > 0)
        {
            var isPrescribedAmount = Math.Abs(row.Amount - prescribed.Total) < Tolerance;
            var monthTaken = existingAdvanceMonths.GetValueOrDefault(row.HouseId)?.Contains(monthKey) == true
                || claimedMonths.Contains($"{row.HouseId}|{monthKey}");

            if (isPrescribedAmount && !monthTaken)
            {
                row.PaymentType = nameof(PaymentType.Advance);
                claimedMonths.Add($"{row.HouseId}|{monthKey}");
            }
            else
            {
                row.PaymentType = nameof(PaymentType.Doplatek);
                row.Warnings.Add(isPrescribedAmount
                    ? $"Záloha za {row.Month:D2}/{row.Year} už existuje — navrženo jako doplatek."
                    : $"Neobvyklá částka (předepsaná záloha {prescribed.Total:N0} Kč) — navrženo jako doplatek.");
            }
        }
        else
        {
            row.PaymentType = nameof(PaymentType.Doplatek);
        }

        var split = SplitAmount(row.Amount, prescribed);
        row.WaterAmount = split.Water;
        row.ElectricityAmount = split.Electricity;
        row.CommonAmount = split.Common;
    }

    /// <summary>
    /// Splits an amount into water/electricity/common in the ratio of the
    /// prescribed advance. Electricity and common are rounded to whole CZK and
    /// water takes the remainder, so the parts always sum to the amount. With no
    /// prescribed advance everything goes to water.
    /// </summary>
    public static AdvanceSplit SplitAmount(decimal amount, AdvanceSplit? prescribed)
    {
        if (prescribed is null || prescribed.Total <= 0)
        {
            return new AdvanceSplit(amount, 0, 0);
        }

        if (Math.Abs(amount - prescribed.Total) < Tolerance)
        {
            return prescribed;
        }

        var electricity = Math.Round(amount * prescribed.Electricity / prescribed.Total, 0, MidpointRounding.AwayFromZero);
        var common = Math.Round(amount * prescribed.Common / prescribed.Total, 0, MidpointRounding.AwayFromZero);
        return new AdvanceSplit(amount - electricity - common, electricity, common);
    }

    // ───────────────────── Confirm ─────────────────────

    public async Task<ConfirmBankImportResponse> ConfirmAsync(ConfirmBankImportRequest request, string importedBy)
    {
        if (!BankAccountNumber.TryParse(request.Account, out var ownAccount))
        {
            throw new AppException("Neplatné číslo účtu výpisu.");
        }

        var ownKey = ownAccount!.ToKey();
        var houses = (await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House)).ToDictionary(h => h.Id);
        var processedIds = (await _transactionRepository.GetByAccountAsync(ownKey))
            .Select(t => t.TransactionId)
            .ToHashSet();
        var mappings = (await _mappingRepository.GetAllMappingsAsync()).ToDictionary(m => m.AccountKey);

        var response = new ConfirmBankImportResponse();
        var toProcess = new List<ConfirmBankImportRow>();
        var errors = new List<string>();
        var seenIds = new HashSet<string>();
        var advanceMonthsByHouse = new Dictionary<string, HashSet<string>>();

        // Validate everything before the first write.
        foreach (var row in request.Rows)
        {
            var id = row.TransactionId?.Trim() ?? string.Empty;
            if (id.Length == 0)
            {
                errors.Add("Řádek bez ID operace.");
                continue;
            }

            if (!seenIds.Add(id))
            {
                errors.Add($"Pohyb {id} je v požadavku vícekrát.");
                continue;
            }

            if (processedIds.Contains(id))
            {
                response.Skipped.Add(id);
                continue;
            }

            if (row.Amount <= 0)
            {
                errors.Add($"Pohyb {id}: importovat lze jen příchozí platby.");
                continue;
            }

            if (row.Action == BankImportAction.Ignore)
            {
                toProcess.Add(row);
                continue;
            }

            if (row.Action != BankImportAction.Import)
            {
                errors.Add($"Pohyb {id}: neznámá akce „{row.Action}“.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.HouseId) || !houses.ContainsKey(row.HouseId))
            {
                errors.Add($"Pohyb {id}: vyberte existující domácnost.");
                continue;
            }

            if (row.WaterAmount < 0 || row.ElectricityAmount < 0 || row.CommonAmount < 0)
            {
                errors.Add($"Pohyb {id}: složky platby nesmí být záporné.");
                continue;
            }

            if (Math.Abs(row.WaterAmount + row.ElectricityAmount + row.CommonAmount - row.Amount) >= Tolerance)
            {
                errors.Add($"Pohyb {id}: součet složek se nerovná částce {row.Amount:N2} Kč.");
                continue;
            }

            if (row.PaymentType == nameof(PaymentType.Advance))
            {
                if (row.Year is < 2000 or > 2100 || row.Month is < 1 or > 12)
                {
                    errors.Add($"Pohyb {id}: neplatný měsíc zálohy.");
                    continue;
                }

                if (!advanceMonthsByHouse.TryGetValue(row.HouseId, out var months))
                {
                    months = (await _advanceRepository.GetByHouseIdAsync(row.HouseId))
                        .Where(p => p.Type == PaymentType.Advance)
                        .Select(p => PaymentRowKeys.Advance(p.Year, p.Month))
                        .ToHashSet();
                    advanceMonthsByHouse[row.HouseId] = months;
                }

                if (!months.Add(PaymentRowKeys.Advance(row.Year, row.Month)))
                {
                    errors.Add($"Pohyb {id}: domácnost „{houses[row.HouseId].Name}“ už má zálohu za {row.Month:D2}/{row.Year}.");
                    continue;
                }
            }
            else if (row.PaymentType != nameof(PaymentType.Doplatek))
            {
                errors.Add($"Pohyb {id}: neznámý typ platby „{row.PaymentType}“.");
                continue;
            }

            toProcess.Add(row);
        }

        if (errors.Count > 0)
        {
            throw new AppException("Import nelze potvrdit: " + string.Join(" ", errors));
        }

        var now = DateTime.UtcNow;
        foreach (var row in toProcess)
        {
            var id = row.TransactionId.Trim();
            var date = DateTime.SpecifyKind(row.Date, DateTimeKind.Utc);
            var tx = new BankTransaction
            {
                OwnAccountKey = ownKey,
                TransactionId = id,
                Date = date,
                Amount = row.Amount,
                CounterAccount = row.CounterAccount,
                CounterName = row.CounterName,
                Message = row.Message,
                VariableSymbol = row.VariableSymbol,
                ImportedAt = now,
                ImportedBy = importedBy,
            };

            if (row.Action == BankImportAction.Ignore)
            {
                tx.Status = BankTransactionStatus.Ignored;
                await _transactionRepository.UpsertAsync(tx);
                response.Ignored++;
                continue;
            }

            var isAdvance = row.PaymentType == nameof(PaymentType.Advance);
            var payment = new AdvancePayment
            {
                HouseId = row.HouseId!,
                Year = isAdvance ? row.Year : date.Year,
                Month = isAdvance ? row.Month : date.Month,
                Amount = row.WaterAmount + row.ElectricityAmount + row.CommonAmount,
                WaterAmount = row.WaterAmount,
                ElectricityAmount = row.ElectricityAmount,
                CommonAmount = row.CommonAmount,
                PaymentDate = date,
                Type = isAdvance ? PaymentType.Advance : PaymentType.Doplatek,
                Note = string.IsNullOrWhiteSpace(row.Message) ? null : row.Message.Trim(),
                RowKey = isAdvance ? PaymentRowKeys.Advance(row.Year, row.Month) : PaymentRowKeys.Doplatek(date),
                BankOwnAccountKey = ownKey,
                BankTransactionId = id,
            };

            // Payment first, then the processed-movement record: if the write fails in
            // between, the movement shows up as New again on the next upload.
            await _advanceRepository.UpsertAsync(payment);

            tx.Status = BankTransactionStatus.Imported;
            tx.HouseId = payment.HouseId;
            tx.PaymentRowKey = payment.RowKey;
            await _transactionRepository.UpsertAsync(tx);
            response.Imported++;

            if (BankAccountNumber.TryParse(row.CounterAccount, out var counterAccount))
            {
                var key = counterAccount!.ToKey();
                var known = mappings.GetValueOrDefault(key);
                if (known is null || known.HouseId != payment.HouseId || known.AccountName != row.CounterName)
                {
                    if (known is null || known.HouseId != payment.HouseId) response.NewAccounts++;
                    var mapping = new BankAccountMapping
                    {
                        AccountKey = key,
                        HouseId = payment.HouseId,
                        AccountName = row.CounterName ?? known?.AccountName,
                        UpdatedAt = now,
                    };
                    await _mappingRepository.UpsertAsync(mapping);
                    mappings[key] = mapping;
                }
            }
        }

        _logger.LogInformation(
            "Bank statement import for {Account}: {Imported} imported, {Ignored} ignored, {Skipped} skipped, {NewAccounts} accounts learned.",
            ownAccount, response.Imported, response.Ignored, response.Skipped.Count, response.NewAccounts);

        return response;
    }

    private static string? JoinAccount(string? account, string? bankCode) =>
        account is null ? null : bankCode is null ? account : $"{account}/{bankCode}";

    private static string? JoinNotes(string? note, string? note2)
    {
        var parts = new[] { note, note2 }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
