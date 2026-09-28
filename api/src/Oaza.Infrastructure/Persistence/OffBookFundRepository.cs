using System.Globalization;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

/// <summary>Off-book funds (table <c>OffBookFunds</c>, PK <c>FUND</c>) and their records (table <c>OffBookFundRecords</c>, PK = fund id).</summary>
public class OffBookFundRepository : IOffBookFundRepository
{
    private readonly TableServiceClient _serviceClient;
    private TableClient? _funds;
    private TableClient? _records;

    public OffBookFundRepository(TableServiceClient serviceClient)
    {
        _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
    }

    private async Task<TableClient> FundsAsync()
    {
        if (_funds is null)
        {
            var client = _serviceClient.GetTableClient(TableNames.OffBookFunds);
            await client.CreateIfNotExistsAsync();
            _funds = client;
        }
        return _funds;
    }

    private async Task<TableClient> RecordsAsync()
    {
        if (_records is null)
        {
            var client = _serviceClient.GetTableClient(TableNames.OffBookFundRecords);
            await client.CreateIfNotExistsAsync();
            _records = client;
        }
        return _records;
    }

    public async Task<IReadOnlyList<OffBookFund>> GetFundsAsync()
    {
        var client = await FundsAsync();
        var result = new List<OffBookFund>();
        await foreach (var e in client.QueryAsync<TableEntity>(filter: TableClient.CreateQueryFilter($"PartitionKey eq {PartitionKeys.OffBookFund}")))
            result.Add(ToFund(e));
        return result;
    }

    public async Task<OffBookFund?> GetFundAsync(string fundId)
    {
        var client = await FundsAsync();
        try
        {
            return ToFund((await client.GetEntityAsync<TableEntity>(PartitionKeys.OffBookFund, fundId)).Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task UpsertFundAsync(OffBookFund fund)
    {
        var client = await FundsAsync();
        await client.UpsertEntityAsync(new TableEntity(PartitionKeys.OffBookFund, fund.Id)
        {
            { "Name", fund.Name },
            { "Purpose", fund.Purpose },
            { "ManagerName", fund.ManagerName },
            { "AccountDescription", fund.AccountDescription },
            { "Active", fund.Active },
        }, TableUpdateMode.Replace);
    }

    public async Task<IReadOnlyList<FundRecord>> GetRecordsAsync(string fundId)
    {
        var client = await RecordsAsync();
        var result = new List<FundRecord>();
        await foreach (var e in client.QueryAsync<TableEntity>(filter: TableClient.CreateQueryFilter($"PartitionKey eq {fundId}")))
            result.Add(ToRecord(e));
        return result;
    }

    public async Task<FundRecord?> GetRecordAsync(string fundId, string recordId)
    {
        var client = await RecordsAsync();
        try
        {
            return ToRecord((await client.GetEntityAsync<TableEntity>(fundId, recordId)).Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task UpsertRecordAsync(FundRecord record)
    {
        var client = await RecordsAsync();
        await client.UpsertEntityAsync(ToEntity(record), TableUpdateMode.Replace);
    }

    public async Task DeleteRecordAsync(string fundId, string recordId)
    {
        var client = await RecordsAsync();
        await client.DeleteEntityAsync(fundId, recordId);
    }

    public static TableEntity ToEntity(FundRecord r) => new(r.FundId, r.Id)
    {
        { "Kind", r.Kind.ToString() },
        { "Date", TableEntityMapper.ToIsoDay(r.Date) },
        { "Amount", r.Amount.ToString("G29", CultureInfo.InvariantCulture) },
        { "Text", r.Text },
        { "DueDate", TableEntityMapper.ToIsoDay(r.DueDate) },
        { "HouseIdsJson", JsonSerializer.Serialize(r.HouseIds) },
        { "HouseId", r.HouseId },
        { "CallId", r.CallId },
        { "Method", r.Method },
        { "PaidBy", r.PaidBy },
        { "HasReceipt", r.HasReceipt },
        { "ExpenseId", r.ExpenseId },
        { "PaidTo", r.PaidTo },
        { "CreatedBy", r.CreatedBy },
        { "CreatedAt", DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc) },
    };

    public static FundRecord ToRecord(TableEntity e) => new()
    {
        Id = e.RowKey,
        FundId = e.PartitionKey,
        Kind = Enum.TryParse<FundRecordKind>(e.GetString("Kind"), out var kind) ? kind : FundRecordKind.Contribution,
        Date = TableEntityMapper.GetIsoDay(e, "Date") ?? DateOnly.MinValue,
        Amount = decimal.TryParse(e.GetString("Amount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) ? amount : 0m,
        Text = e.GetString("Text") ?? string.Empty,
        DueDate = TableEntityMapper.GetIsoDay(e, "DueDate"),
        HouseIds = JsonSerializer.Deserialize<List<string>>(e.GetString("HouseIdsJson") ?? "[]") ?? [],
        HouseId = e.GetString("HouseId"),
        CallId = e.GetString("CallId"),
        Method = e.GetString("Method"),
        PaidBy = e.GetString("PaidBy"),
        HasReceipt = e.GetBoolean("HasReceipt") ?? false,
        ExpenseId = e.GetString("ExpenseId"),
        PaidTo = e.GetString("PaidTo"),
        CreatedBy = e.GetString("CreatedBy") ?? string.Empty,
        CreatedAt = e.GetDateTimeOffset("CreatedAt")?.UtcDateTime ?? DateTime.MinValue,
    };

    private static OffBookFund ToFund(TableEntity e) => new()
    {
        Id = e.RowKey,
        Name = e.GetString("Name") ?? string.Empty,
        Purpose = e.GetString("Purpose"),
        ManagerName = e.GetString("ManagerName") ?? string.Empty,
        AccountDescription = e.GetString("AccountDescription"),
        Active = e.GetBoolean("Active") ?? true,
    };
}
