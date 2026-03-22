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
            { "IsActive", house.IsActive }
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
            IsActive = entity.GetBoolean("IsActive") ?? true
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
            { "InstallationDate", meter.InstallationDate }
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
            InstallationDate = entity.GetDateTimeOffset("InstallationDate")?.UtcDateTime ?? DateTime.MinValue
        };
    }

    // ───────────────────── MeterReading ─────────────────────
    // PK = meterId, RK = inverted timestamp

    public static TableEntity ToTableEntity(MeterReading reading)
    {
        return new TableEntity(reading.MeterId, InvertedTimestamp.FromDateTime(reading.ReadingDate))
        {
            { "ReadingDate", reading.ReadingDate },
            { "Value", reading.Value.ToString("G29", CultureInfo.InvariantCulture) },
            { "Source", reading.Source.ToString() },
            { "ImportedAt", reading.ImportedAt },
            { "ImportedBy", reading.ImportedBy }
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
            ImportedBy = entity.GetString("ImportedBy") ?? string.Empty
        };
    }

    // ───────────────────── BillingPeriod ─────────────────────

    public static TableEntity ToTableEntity(BillingPeriod period)
    {
        return new TableEntity(PartitionKeys.Period, period.Id)
        {
            { "Name", period.Name },
            { "DateFrom", period.DateFrom },
            { "DateTo", period.DateTo },
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
            { "IssuedDate", invoice.IssuedDate },
            { "DueDate", invoice.DueDate },
            { "Amount", invoice.Amount.ToString("G29", CultureInfo.InvariantCulture) },
            { "ConsumptionM3", invoice.ConsumptionM3.ToString("G29", CultureInfo.InvariantCulture) },
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
            AttachmentBlobName = entity.GetString("AttachmentBlobName")
        };
    }

    // ───────────────────── AdvancePayment ─────────────────────
    // PK = houseId, RK = "YYYY-MM"

    public static TableEntity ToTableEntity(AdvancePayment payment)
    {
        var rowKey = $"{payment.Year:D4}-{payment.Month:D2}";
        return new TableEntity(payment.HouseId, rowKey)
        {
            { "Year", payment.Year },
            { "Month", payment.Month },
            { "Amount", payment.Amount.ToString("G29", CultureInfo.InvariantCulture) },
            { "PaymentDate", payment.PaymentDate }
        };
    }

    public static AdvancePayment ToAdvancePayment(TableEntity entity)
    {
        return new AdvancePayment
        {
            HouseId = entity.PartitionKey,
            Year = entity.GetInt32("Year") ?? 0,
            Month = entity.GetInt32("Month") ?? 0,
            Amount = decimal.TryParse(entity.GetString("Amount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) ? amount : 0m,
            PaymentDate = entity.GetDateTimeOffset("PaymentDate")?.UtcDateTime ?? DateTime.MinValue
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
            { "LossAllocatedM3", settlement.LossAllocatedM3.ToString("G29", CultureInfo.InvariantCulture) }
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
            LossAllocatedM3 = decimal.TryParse(entity.GetString("LossAllocatedM3"), NumberStyles.Any, CultureInfo.InvariantCulture, out var lossAllocatedM3) ? lossAllocatedM3 : 0m
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
            { "Date", record.Date },
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
}
