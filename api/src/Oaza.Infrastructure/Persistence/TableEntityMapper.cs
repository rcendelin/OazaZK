using System.Globalization;
using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Helpers;

namespace Oaza.Infrastructure.Persistence;

/// <summary>
/// Explicit mapping between domain entities and Azure TableEntity.
/// No reflection — each entity type has hand-written mapping methods.
/// </summary>
public static class TableEntityMapper
{
    // ───────────────────── User ─────────────────────

    public static TableEntity ToTableEntity(User user)
    {
        var entity = new TableEntity(PartitionKeys.User, user.Id)
        {
            { "Name", user.Name },
            { "Email", user.Email },
            { "Role", user.Role.ToString() },
            { "HouseId", user.HouseId },
            { "AuthMethod", user.AuthMethod.ToString() },
            { "EntraObjectId", user.EntraObjectId },
            { "MagicLinkTokenHash", user.MagicLinkTokenHash },
            { "MagicLinkExpiry", user.MagicLinkExpiry },
            { "LastLogin", user.LastLogin },
            { "NotificationsEnabled", user.NotificationsEnabled },
            { "MagicLinkRequestCount", user.MagicLinkRequestCount },
            { "MagicLinkRequestWindowStart", user.MagicLinkRequestWindowStart },
            { "MagicLinkFailedAttempts", user.MagicLinkFailedAttempts }
        };
        return entity;
    }

    public static User ToUser(TableEntity entity)
    {
        return new User
        {
            Id = entity.RowKey,
            Name = entity.GetString("Name") ?? string.Empty,
            Email = entity.GetString("Email") ?? string.Empty,
            Role = Enum.TryParse<UserRole>(entity.GetString("Role"), out var role) ? role : UserRole.Member,
            HouseId = entity.GetString("HouseId"),
            AuthMethod = Enum.TryParse<AuthMethod>(entity.GetString("AuthMethod"), out var authMethod) ? authMethod : AuthMethod.MagicLink,
            EntraObjectId = entity.GetString("EntraObjectId"),
            MagicLinkTokenHash = entity.GetString("MagicLinkTokenHash"),
            MagicLinkExpiry = entity.GetDateTimeOffset("MagicLinkExpiry")?.UtcDateTime,
            LastLogin = entity.GetDateTimeOffset("LastLogin")?.UtcDateTime,
            NotificationsEnabled = entity.GetBoolean("NotificationsEnabled") ?? true,
            MagicLinkRequestCount = entity.GetInt32("MagicLinkRequestCount") ?? 0,
            MagicLinkRequestWindowStart = entity.GetDateTimeOffset("MagicLinkRequestWindowStart")?.UtcDateTime,
            MagicLinkFailedAttempts = entity.GetInt32("MagicLinkFailedAttempts") ?? 0
        };
    }

    // ───────────────────── House ─────────────────────

    public static TableEntity ToTableEntity(House house)
    {
        return new TableEntity(PartitionKeys.House, house.Id)
        {
            { "Name", house.Name },
            { "Address", house.Address },
            { "ContactPerson", house.ContactPerson },
            { "Email", house.Email },
            { "IsActive", house.IsActive },
            { "DissolveOverpayment", house.DissolveOverpayment }
        };
    }

    public static House ToHouse(TableEntity entity)
    {
        return new House
        {
            Id = entity.RowKey,
            Name = entity.GetString("Name") ?? string.Empty,
            Address = entity.GetString("Address") ?? string.Empty,
            ContactPerson = entity.GetString("ContactPerson") ?? string.Empty,
            Email = entity.GetString("Email") ?? string.Empty,
            IsActive = entity.GetBoolean("IsActive") ?? true,
            DissolveOverpayment = entity.GetBoolean("DissolveOverpayment") ?? false
        };
    }

    // ───────────────────── WaterMeter ─────────────────────

    public static TableEntity ToTableEntity(WaterMeter meter)
    {
        return new TableEntity(PartitionKeys.Meter, meter.Id)
        {
            { "MeterNumber", meter.MeterNumber },
            { "Name", meter.Name },
            { "Type", meter.Type.ToString() },
            { "HouseId", meter.HouseId },
            { "RadioAddress", meter.RadioAddress },
            { "InstallationDate", DateTime.SpecifyKind(meter.InstallationDate, DateTimeKind.Utc) }
        };
    }

    public static WaterMeter ToWaterMeter(TableEntity entity)
    {
        return new WaterMeter
        {
            Id = entity.RowKey,
            MeterNumber = entity.GetString("MeterNumber") ?? string.Empty,
            Name = entity.GetString("Name") ?? string.Empty,
            Type = Enum.TryParse<MeterType>(entity.GetString("Type"), out var meterType) ? meterType : MeterType.Individual,
            HouseId = entity.GetString("HouseId"),
            RadioAddress = entity.GetString("RadioAddress"),
            InstallationDate = entity.GetDateTimeOffset("InstallationDate")?.UtcDateTime ?? DateTime.MinValue
        };
    }

    // ───────────────────── MeterReading ─────────────────────
    // PK = meterId, RK = inverted timestamp

    public static TableEntity ToTableEntity(MeterReading reading)
    {
        return new TableEntity(reading.MeterId, InvertedTimestamp.FromDateTime(reading.ReadingDate))
        {
            { "ReadingDate", DateTime.SpecifyKind(reading.ReadingDate, DateTimeKind.Utc) },
            { "Value", reading.Value.ToString("G29", CultureInfo.InvariantCulture) },
            { "Source", reading.Source.ToString() },
            { "ImportedAt", DateTime.SpecifyKind(reading.ImportedAt, DateTimeKind.Utc) },
            { "ImportedBy", reading.ImportedBy },
            { "IsEstimate", reading.IsEstimate },
            { "EstimateNote", reading.EstimateNote }
        };
    }

    public static MeterReading ToMeterReading(TableEntity entity)
    {
        return new MeterReading
        {
            MeterId = entity.PartitionKey,
            ReadingDate = entity.GetDateTimeOffset("ReadingDate")?.UtcDateTime ?? InvertedTimestamp.ToDateTime(entity.RowKey),
            Value = decimal.TryParse(entity.GetString("Value"), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0m,
            Source = Enum.TryParse<ReadingSource>(entity.GetString("Source"), out var source) ? source : ReadingSource.Manual,
            ImportedAt = entity.GetDateTimeOffset("ImportedAt")?.UtcDateTime ?? DateTime.MinValue,
            ImportedBy = entity.GetString("ImportedBy") ?? string.Empty,
            IsEstimate = entity.GetBoolean("IsEstimate") ?? false,
            EstimateNote = entity.GetString("EstimateNote")
        };
    }

    // ───────────────────── BillingPeriod ─────────────────────

    public static TableEntity ToTableEntity(BillingPeriod period)
    {
        return new TableEntity(PartitionKeys.Period, period.Id)
        {
            { "Name", period.Name },
            { "DateFrom", DateTime.SpecifyKind(period.DateFrom, DateTimeKind.Utc) },
            { "DateTo", DateTime.SpecifyKind(period.DateTo, DateTimeKind.Utc) },
            { "Status", period.Status.ToString() }
        };
    }

    public static BillingPeriod ToBillingPeriod(TableEntity entity)
    {
        return new BillingPeriod
        {
            Id = entity.RowKey,
            Name = entity.GetString("Name") ?? string.Empty,
            DateFrom = entity.GetDateTimeOffset("DateFrom")?.UtcDateTime ?? DateTime.MinValue,
            DateTo = entity.GetDateTimeOffset("DateTo")?.UtcDateTime ?? DateTime.MinValue,
            Status = Enum.TryParse<BillingPeriodStatus>(entity.GetString("Status"), out var status) ? status : BillingPeriodStatus.Open
        };
    }

    // ───────────────────── SupplierInvoice ─────────────────────

    public static TableEntity ToTableEntity(SupplierInvoice invoice)
    {
        return new TableEntity(PartitionKeys.Invoice, invoice.Id)
        {
            { "Year", invoice.Year },
            { "Month", invoice.Month },
            { "InvoiceNumber", invoice.InvoiceNumber },
            { "IssuedDate", DateTime.SpecifyKind(invoice.IssuedDate, DateTimeKind.Utc) },
            { "DueDate", DateTime.SpecifyKind(invoice.DueDate, DateTimeKind.Utc) },
            { "Amount", invoice.Amount.ToString("G29", CultureInfo.InvariantCulture) },
            { "ConsumptionM3", invoice.ConsumptionM3.ToString("G29", CultureInfo.InvariantCulture) },
            { "VatRatePercent", invoice.VatRatePercent.ToString("G29", CultureInfo.InvariantCulture) },
            { "LineItemsJson", System.Text.Json.JsonSerializer.Serialize(invoice.LineItems) },
            { "AttachmentBlobName", invoice.AttachmentBlobName }
        };
    }

    public static SupplierInvoice ToSupplierInvoice(TableEntity entity)
    {
        return new SupplierInvoice
        {
            Id = entity.RowKey,
            Year = entity.GetInt32("Year") ?? 0,
            Month = entity.GetInt32("Month") ?? 0,
            InvoiceNumber = entity.GetString("InvoiceNumber") ?? string.Empty,
            IssuedDate = entity.GetDateTimeOffset("IssuedDate")?.UtcDateTime ?? DateTime.MinValue,
            DueDate = entity.GetDateTimeOffset("DueDate")?.UtcDateTime ?? DateTime.MinValue,
            Amount = decimal.TryParse(entity.GetString("Amount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) ? amount : 0m,
            ConsumptionM3 = decimal.TryParse(entity.GetString("ConsumptionM3"), NumberStyles.Any, CultureInfo.InvariantCulture, out var consumption) ? consumption : 0m,
            VatRatePercent = decimal.TryParse(entity.GetString("VatRatePercent"), NumberStyles.Any, CultureInfo.InvariantCulture, out var vat) ? vat : 0m,
            LineItems = System.Text.Json.JsonSerializer.Deserialize<List<InvoiceLineItem>>(entity.GetString("LineItemsJson") ?? "[]") ?? new(),
            AttachmentBlobName = entity.GetString("AttachmentBlobName")
        };
    }

    // ───────────────────── AdvancePayment ─────────────────────
    // PK = houseId. RK = "YYYY-MM" for advances, "D-{invertedTicks}-{guid8}" for doplatky.

    public static TableEntity ToTableEntity(AdvancePayment payment)
    {
        // Advances are keyed by month (one per house per month). Doplatky carry a
        // pre-assigned unique RowKey (set by the endpoint at creation).
        var rowKey = payment.Type == PaymentType.Advance
            ? $"{payment.Year:D4}-{payment.Month:D2}"
            : payment.RowKey;

        return new TableEntity(payment.HouseId, rowKey)
        {
            { "Year", payment.Year },
            { "Month", payment.Month },
            { "Amount", payment.Amount.ToString("G29", CultureInfo.InvariantCulture) },
            { "WaterAmount", payment.WaterAmount.ToString("G29", CultureInfo.InvariantCulture) },
            { "ElectricityAmount", payment.ElectricityAmount.ToString("G29", CultureInfo.InvariantCulture) },
            { "CommonAmount", payment.CommonAmount.ToString("G29", CultureInfo.InvariantCulture) },
            { "PaymentDate", DateTime.SpecifyKind(payment.PaymentDate, DateTimeKind.Utc) },
            { "Type", payment.Type.ToString() },
            { "Note", payment.Note },
            { "IsFundTransfer", payment.IsFundTransfer },
            { "BankOwnAccountKey", payment.BankOwnAccountKey },
            { "BankTransactionId", payment.BankTransactionId }
        };
    }

    public static AdvancePayment ToAdvancePayment(TableEntity entity)
    {
        return new AdvancePayment
        {
            HouseId = entity.PartitionKey,
            RowKey = entity.RowKey,
            Year = entity.GetInt32("Year") ?? 0,
            Month = entity.GetInt32("Month") ?? 0,
            Amount = decimal.TryParse(entity.GetString("Amount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) ? amount : 0m,
            WaterAmount = decimal.TryParse(entity.GetString("WaterAmount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var water) ? water : 0m,
            ElectricityAmount = decimal.TryParse(entity.GetString("ElectricityAmount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var elec) ? elec : 0m,
            CommonAmount = decimal.TryParse(entity.GetString("CommonAmount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var common) ? common : 0m,
            PaymentDate = entity.GetDateTimeOffset("PaymentDate")?.UtcDateTime ?? DateTime.MinValue,
            Type = Enum.TryParse<PaymentType>(entity.GetString("Type"), out var type) ? type : PaymentType.Advance,
            Note = entity.GetString("Note"),
            IsFundTransfer = entity.GetBoolean("IsFundTransfer") ?? false,
            BankOwnAccountKey = entity.GetString("BankOwnAccountKey"),
            BankTransactionId = entity.GetString("BankTransactionId")
        };
    }

    // ───────────────────── BankAccountMapping ─────────────────────
    // PK = "MAP", RK = normalized account key.

    public static TableEntity ToTableEntity(BankAccountMapping mapping)
    {
        return new TableEntity(PartitionKeys.BankAccountMapping, mapping.AccountKey)
        {
            { "HouseId", mapping.HouseId },
            { "AccountName", mapping.AccountName },
            { "UpdatedAt", DateTime.SpecifyKind(mapping.UpdatedAt, DateTimeKind.Utc) }
        };
    }

    public static BankAccountMapping ToBankAccountMapping(TableEntity entity)
    {
        return new BankAccountMapping
        {
            AccountKey = entity.RowKey,
            HouseId = entity.GetString("HouseId") ?? string.Empty,
            AccountName = entity.GetString("AccountName"),
            UpdatedAt = entity.GetDateTimeOffset("UpdatedAt")?.UtcDateTime ?? DateTime.MinValue
        };
    }

    // ───────────────────── BankTransaction ─────────────────────
    // PK = own account key, RK = bank operation id.

    public static TableEntity ToTableEntity(BankTransaction tx)
    {
        return new TableEntity(tx.OwnAccountKey, tx.TransactionId)
        {
            { "Date", DateTime.SpecifyKind(tx.Date, DateTimeKind.Utc) },
            { "Amount", tx.Amount.ToString("G29", CultureInfo.InvariantCulture) },
            { "CounterAccount", tx.CounterAccount },
            { "CounterName", tx.CounterName },
            { "Message", tx.Message },
            { "VariableSymbol", tx.VariableSymbol },
            { "Status", tx.Status.ToString() },
            { "HouseId", tx.HouseId },
            { "PaymentRowKey", tx.PaymentRowKey },
            { "ImportedAt", DateTime.SpecifyKind(tx.ImportedAt, DateTimeKind.Utc) },
            { "ImportedBy", tx.ImportedBy }
        };
    }

    public static BankTransaction ToBankTransaction(TableEntity entity)
    {
        return new BankTransaction
        {
            OwnAccountKey = entity.PartitionKey,
            TransactionId = entity.RowKey,
            Date = entity.GetDateTimeOffset("Date")?.UtcDateTime ?? DateTime.MinValue,
            Amount = decimal.TryParse(entity.GetString("Amount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) ? amount : 0m,
            CounterAccount = entity.GetString("CounterAccount"),
            CounterName = entity.GetString("CounterName"),
            Message = entity.GetString("Message"),
            VariableSymbol = entity.GetString("VariableSymbol"),
            Status = Enum.TryParse<BankTransactionStatus>(entity.GetString("Status"), out var status) ? status : BankTransactionStatus.Imported,
            HouseId = entity.GetString("HouseId"),
            PaymentRowKey = entity.GetString("PaymentRowKey"),
            ImportedAt = entity.GetDateTimeOffset("ImportedAt")?.UtcDateTime ?? DateTime.MinValue,
            ImportedBy = entity.GetString("ImportedBy") ?? string.Empty
        };
    }

    // ───────────────────── Settlement ─────────────────────
    // PK = periodId, RK = houseId

    public static TableEntity ToTableEntity(Settlement settlement)
    {
        return new TableEntity(settlement.PeriodId, settlement.HouseId)
        {
            { "ConsumptionM3", settlement.ConsumptionM3.ToString("G29", CultureInfo.InvariantCulture) },
            { "SharePercent", settlement.SharePercent.ToString("G29", CultureInfo.InvariantCulture) },
            { "CalculatedAmount", settlement.CalculatedAmount.ToString("G29", CultureInfo.InvariantCulture) },
            { "TotalAdvances", settlement.TotalAdvances.ToString("G29", CultureInfo.InvariantCulture) },
            { "Balance", settlement.Balance.ToString("G29", CultureInfo.InvariantCulture) },
            { "LossAllocatedM3", settlement.LossAllocatedM3.ToString("G29", CultureInfo.InvariantCulture) },
            { "ElectricityCharge", settlement.ElectricityCharge.ToString("G29", CultureInfo.InvariantCulture) },
            { "ElectricityAdvances", settlement.ElectricityAdvances.ToString("G29", CultureInfo.InvariantCulture) },
            { "CommonCharge", settlement.CommonCharge.ToString("G29", CultureInfo.InvariantCulture) },
            { "CommonAdvances", settlement.CommonAdvances.ToString("G29", CultureInfo.InvariantCulture) }
        };
    }

    public static Settlement ToSettlement(TableEntity entity)
    {
        return new Settlement
        {
            PeriodId = entity.PartitionKey,
            HouseId = entity.RowKey,
            ConsumptionM3 = decimal.TryParse(entity.GetString("ConsumptionM3"), NumberStyles.Any, CultureInfo.InvariantCulture, out var consumptionM3) ? consumptionM3 : 0m,
            SharePercent = decimal.TryParse(entity.GetString("SharePercent"), NumberStyles.Any, CultureInfo.InvariantCulture, out var sharePercent) ? sharePercent : 0m,
            CalculatedAmount = decimal.TryParse(entity.GetString("CalculatedAmount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var calculatedAmount) ? calculatedAmount : 0m,
            TotalAdvances = decimal.TryParse(entity.GetString("TotalAdvances"), NumberStyles.Any, CultureInfo.InvariantCulture, out var totalAdvances) ? totalAdvances : 0m,
            Balance = decimal.TryParse(entity.GetString("Balance"), NumberStyles.Any, CultureInfo.InvariantCulture, out var balance) ? balance : 0m,
            LossAllocatedM3 = decimal.TryParse(entity.GetString("LossAllocatedM3"), NumberStyles.Any, CultureInfo.InvariantCulture, out var lossAllocatedM3) ? lossAllocatedM3 : 0m,
            ElectricityCharge = decimal.TryParse(entity.GetString("ElectricityCharge"), NumberStyles.Any, CultureInfo.InvariantCulture, out var elecCharge) ? elecCharge : 0m,
            ElectricityAdvances = decimal.TryParse(entity.GetString("ElectricityAdvances"), NumberStyles.Any, CultureInfo.InvariantCulture, out var elecAdv) ? elecAdv : 0m,
            CommonCharge = decimal.TryParse(entity.GetString("CommonCharge"), NumberStyles.Any, CultureInfo.InvariantCulture, out var commonCharge) ? commonCharge : 0m,
            CommonAdvances = decimal.TryParse(entity.GetString("CommonAdvances"), NumberStyles.Any, CultureInfo.InvariantCulture, out var commonAdv) ? commonAdv : 0m
        };
    }

    // ───────────────────── Document ─────────────────────
    // PK = category, RK = GUID

    public static TableEntity ToTableEntity(Document document)
    {
        return new TableEntity(document.Category, document.Id)
        {
            { "Name", document.Name },
            { "BlobName", document.BlobName },
            { "FileSizeBytes", document.FileSizeBytes },
            { "ContentType", document.ContentType },
            { "UploadedAt", document.UploadedAt },
            { "UploadedBy", document.UploadedBy }
        };
    }

    public static Document ToDocument(TableEntity entity)
    {
        return new Document
        {
            Id = entity.RowKey,
            Category = entity.PartitionKey,
            Name = entity.GetString("Name") ?? string.Empty,
            BlobName = entity.GetString("BlobName") ?? string.Empty,
            FileSizeBytes = entity.GetInt64("FileSizeBytes") ?? 0,
            ContentType = entity.GetString("ContentType") ?? string.Empty,
            UploadedAt = entity.GetDateTimeOffset("UploadedAt")?.UtcDateTime ?? DateTime.MinValue,
            UploadedBy = entity.GetString("UploadedBy") ?? string.Empty
        };
    }

    // ───────────────────── DocumentVersion ─────────────────────
    // PK = documentId, RK = version number (zero-padded, e.g., "001")

    public static TableEntity ToTableEntity(DocumentVersion version)
    {
        return new TableEntity(version.DocumentId, version.VersionNumber.ToString("D3"))
        {
            { "VersionNumber", version.VersionNumber },
            { "BlobName", version.BlobName },
            { "FileSizeBytes", version.FileSizeBytes },
            { "ContentType", version.ContentType },
            { "UploadedAt", version.UploadedAt },
            { "UploadedBy", version.UploadedBy }
        };
    }

    public static DocumentVersion ToDocumentVersion(TableEntity entity)
    {
        return new DocumentVersion
        {
            DocumentId = entity.PartitionKey,
            VersionNumber = entity.GetInt32("VersionNumber") ?? int.Parse(entity.RowKey),
            BlobName = entity.GetString("BlobName") ?? string.Empty,
            FileSizeBytes = entity.GetInt64("FileSizeBytes") ?? 0,
            ContentType = entity.GetString("ContentType") ?? string.Empty,
            UploadedAt = entity.GetDateTimeOffset("UploadedAt")?.UtcDateTime ?? DateTime.MinValue,
            UploadedBy = entity.GetString("UploadedBy") ?? string.Empty
        };
    }

    // ───────────────────── FinancialRecord ─────────────────────
    // PK = year (as string), RK = GUID

    public static TableEntity ToTableEntity(FinancialRecord record)
    {
        return new TableEntity(record.Year.ToString(), record.Id)
        {
            { "Year", record.Year },
            { "Type", record.Type.ToString() },
            { "Category", record.Category },
            { "Amount", record.Amount.ToString("G29", CultureInfo.InvariantCulture) },
            { "Date", DateTime.SpecifyKind(record.Date, DateTimeKind.Utc) },
            { "Description", record.Description },
            { "AttachmentBlobName", record.AttachmentBlobName }
        };
    }

    public static FinancialRecord ToFinancialRecord(TableEntity entity)
    {
        return new FinancialRecord
        {
            Id = entity.RowKey,
            Year = entity.GetInt32("Year") ?? 0,
            Type = Enum.TryParse<FinancialRecordType>(entity.GetString("Type"), out var type) ? type : FinancialRecordType.Expense,
            Category = entity.GetString("Category") ?? string.Empty,
            Amount = decimal.TryParse(entity.GetString("Amount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) ? amount : 0m,
            Date = entity.GetDateTimeOffset("Date")?.UtcDateTime ?? DateTime.MinValue,
            Description = entity.GetString("Description") ?? string.Empty,
            AttachmentBlobName = entity.GetString("AttachmentBlobName")
        };
    }

    // ───────────────────── AdvanceSettings ─────────────────────
    // PK = "SETTINGS", RK = "advances" (singleton)

    public static TableEntity ToTableEntity(AdvanceSettings settings)
    {
        var jsonOpts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        var entity = new TableEntity("SETTINGS", "advances")
        {
            { "WaterPricePerM3", settings.WaterPricePerM3.ToString("G29", CultureInfo.InvariantCulture) },
            { "WaterPriceValidFrom", DateTime.SpecifyKind(settings.WaterPriceValidFrom, DateTimeKind.Utc) },
            { "MonthlyElectricityCost", settings.MonthlyElectricityCost.ToString("G29", CultureInfo.InvariantCulture) },
            { "MonthlyCommonBaseFee", settings.MonthlyCommonBaseFee.ToString("G29", CultureInfo.InvariantCulture) },
            { "LossAllocationMethod", settings.LossAllocationMethod },
            { "ElectricityCoefficientsJson", System.Text.Json.JsonSerializer.Serialize(settings.ElectricityCoefficients) },
            { "HouseOverridesJson", System.Text.Json.JsonSerializer.Serialize(settings.HouseOverrides, jsonOpts) }
        };
        if (settings.WaterPriceValidTo.HasValue)
            entity["WaterPriceValidTo"] = DateTime.SpecifyKind(settings.WaterPriceValidTo.Value, DateTimeKind.Utc);
        return entity;
    }

    public static AdvanceSettings ToAdvanceSettings(TableEntity entity)
    {
        var jsonOpts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
        var coeffJson = entity.GetString("ElectricityCoefficientsJson") ?? "{}";
        var coefficients = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(coeffJson)
            ?? new Dictionary<string, decimal>();

        var overridesJson = entity.GetString("HouseOverridesJson") ?? "{}";
        var overrides = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, HouseAdvanceOverride>>(overridesJson, jsonOpts)
            ?? new Dictionary<string, HouseAdvanceOverride>();

        return new AdvanceSettings
        {
            WaterPricePerM3 = decimal.TryParse(entity.GetString("WaterPricePerM3"), NumberStyles.Any, CultureInfo.InvariantCulture, out var price) ? price : 0m,
            WaterPriceValidFrom = entity.GetDateTimeOffset("WaterPriceValidFrom")?.UtcDateTime ?? DateTime.MinValue,
            WaterPriceValidTo = entity.GetDateTimeOffset("WaterPriceValidTo")?.UtcDateTime,
            MonthlyElectricityCost = decimal.TryParse(entity.GetString("MonthlyElectricityCost"), NumberStyles.Any, CultureInfo.InvariantCulture, out var elec) ? elec : 0m,
            MonthlyCommonBaseFee = decimal.TryParse(entity.GetString("MonthlyCommonBaseFee"), NumberStyles.Any, CultureInfo.InvariantCulture, out var common) ? common : 0m,
            LossAllocationMethod = entity.GetString("LossAllocationMethod") ?? "ProportionalToConsumption",
            ElectricityCoefficients = coefficients,
            HouseOverrides = overrides,
        };
    }

    // ───────────────────── Calendar days (X5) ─────────────────────
    // DateOnly is stored as a "yyyy-MM-dd" string: sortable, no time zone.

    public static string? ToIsoDay(DateOnly? day) =>
        day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly? GetIsoDay(TableEntity entity, string key) =>
        DateOnly.TryParseExact(entity.GetString(key), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    private static string? ToInvariant(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static decimal? GetDecimal(TableEntity entity, string key) =>
        decimal.TryParse(entity.GetString(key), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;

    // ───────────────────── CostComponent (T02) ─────────────────────

    public static TableEntity ToTableEntity(CostComponent component)
    {
        return new TableEntity(PartitionKeys.CostComponent, component.Id)
        {
            { "Name", component.Name },
            { "Code", component.Code },
            { "StartDate", ToIsoDay(component.StartDate) },
            { "AllocationBasis", component.AllocationBasis.ToString() },
            { "Active", component.Active },
            { "Note", component.Note },
        };
    }

    public static CostComponent ToCostComponent(TableEntity entity)
    {
        return new CostComponent
        {
            Id = entity.RowKey,
            Name = entity.GetString("Name") ?? string.Empty,
            Code = entity.GetString("Code") ?? string.Empty,
            StartDate = GetIsoDay(entity, "StartDate") ?? DateOnly.MinValue,
            AllocationBasis = Enum.TryParse<AllocationBasis>(entity.GetString("AllocationBasis"), out var basis) ? basis : AllocationBasis.CostEntries,
            Active = entity.GetBoolean("Active") ?? true,
            Note = entity.GetString("Note"),
        };
    }

    // ───────────────────── ComponentAllocationRule (T02) ─────────────────────
    // PK = component id, RK = rule id

    public static TableEntity ToTableEntity(ComponentAllocationRule rule)
    {
        return new TableEntity(rule.ComponentId, rule.Id)
        {
            { "ValidFrom", ToIsoDay(rule.ValidFrom) },
            { "ValidTo", ToIsoDay(rule.ValidTo) },
            { "Method", rule.Method.ToString() },
            { "RatioSource", rule.RatioSource },
            { "Reason", rule.Reason },
        };
    }

    public static ComponentAllocationRule ToComponentAllocationRule(TableEntity entity)
    {
        return new ComponentAllocationRule
        {
            Id = entity.RowKey,
            ComponentId = entity.PartitionKey,
            ValidFrom = GetIsoDay(entity, "ValidFrom") ?? DateOnly.MinValue,
            ValidTo = GetIsoDay(entity, "ValidTo"),
            Method = Enum.TryParse<AllocationMethod>(entity.GetString("Method"), out var method) ? method : AllocationMethod.Equal,
            RatioSource = entity.GetString("RatioSource"),
            Reason = entity.GetString("Reason"),
        };
    }

    // ───────────────────── Participation (T02) ─────────────────────
    // PK = component id, RK = participation id

    public static TableEntity ToTableEntity(Participation participation)
    {
        return new TableEntity(participation.ComponentId, participation.Id)
        {
            { "HouseId", participation.HouseId },
            { "ValidFrom", ToIsoDay(participation.ValidFrom) },
            { "ValidTo", ToIsoDay(participation.ValidTo) },
            { "Weight", ToInvariant(participation.Weight) },
        };
    }

    public static Participation ToParticipation(TableEntity entity)
    {
        return new Participation
        {
            Id = entity.RowKey,
            ComponentId = entity.PartitionKey,
            HouseId = entity.GetString("HouseId") ?? string.Empty,
            ValidFrom = GetIsoDay(entity, "ValidFrom") ?? DateOnly.MinValue,
            ValidTo = GetIsoDay(entity, "ValidTo"),
            Weight = GetDecimal(entity, "Weight"),
        };
    }

    // ───────────────────── OwnershipPeriod (T03) ─────────────────────
    // PK = house id, RK = valid-from day

    public static TableEntity ToTableEntity(OwnershipPeriod period)
    {
        return new TableEntity(period.HouseId, ToIsoDay(period.ValidFrom))
        {
            { "OwnerName", period.OwnerName },
            { "Contact", period.Contact },
            { "ValidTo", ToIsoDay(period.ValidTo) },
        };
    }

    public static OwnershipPeriod ToOwnershipPeriod(TableEntity entity)
    {
        var validFrom = DateOnly.TryParseExact(entity.RowKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : DateOnly.MinValue;
        return new OwnershipPeriod
        {
            Id = OwnershipPeriod.KeyFor(entity.PartitionKey, validFrom),
            HouseId = entity.PartitionKey,
            OwnerName = entity.GetString("OwnerName") ?? string.Empty,
            Contact = entity.GetString("Contact"),
            ValidFrom = validFrom,
            ValidTo = GetIsoDay(entity, "ValidTo"),
        };
    }

    // ───────────────────── OpeningBalance (T03) ─────────────────────
    // PK = OPENING, RK = natural key

    public static TableEntity ToTableEntity(OpeningBalance balance)
    {
        return new TableEntity(PartitionKeys.OpeningBalance, balance.Key)
        {
            { "Type", balance.Type.ToString() },
            { "HouseId", balance.HouseId },
            { "ComponentId", balance.ComponentId },
            { "MeterId", balance.MeterId },
            { "OwnershipPeriodId", balance.OwnershipPeriodId },
            { "Date", ToIsoDay(balance.Date) },
            { "Value", balance.Value.ToString("G29", CultureInfo.InvariantCulture) },
            { "IsEstimate", balance.IsEstimate },
            { "Source", balance.Source },
            { "Note", balance.Note },
        };
    }

    public static OpeningBalance ToOpeningBalance(TableEntity entity)
    {
        return new OpeningBalance
        {
            Key = entity.RowKey,
            Type = Enum.TryParse<OpeningBalanceType>(entity.GetString("Type"), out var type) ? type : OpeningBalanceType.FundShare,
            HouseId = entity.GetString("HouseId"),
            ComponentId = entity.GetString("ComponentId"),
            MeterId = entity.GetString("MeterId"),
            OwnershipPeriodId = entity.GetString("OwnershipPeriodId"),
            Date = GetIsoDay(entity, "Date") ?? DateOnly.MinValue,
            Value = GetDecimal(entity, "Value") ?? 0m,
            IsEstimate = entity.GetBoolean("IsEstimate") ?? false,
            Source = entity.GetString("Source") ?? string.Empty,
            Note = entity.GetString("Note"),
        };
    }
}
