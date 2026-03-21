using Oaza.Application.DTOs;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;

namespace Oaza.Application.Mapping;

public static class EntityMapper
{
    public static HouseResponse ToResponse(House house)
    {
        return new HouseResponse
        {
            Id = house.Id,
            Name = house.Name,
            Address = house.Address,
            ContactPerson = house.ContactPerson,
            Email = house.Email,
            IsActive = house.IsActive,
        };
    }

    public static MeterResponse ToResponse(WaterMeter meter)
    {
        return new MeterResponse
        {
            Id = meter.Id,
            MeterNumber = meter.MeterNumber,
            Type = meter.Type.ToString(),
            HouseId = meter.HouseId,
            InstallationDate = meter.InstallationDate,
        };
    }

    public static InvoiceResponse ToResponse(SupplierInvoice invoice)
    {
        return new InvoiceResponse
        {
            Id = invoice.Id,
            Year = invoice.Year,
            Month = invoice.Month,
            InvoiceNumber = invoice.InvoiceNumber,
            IssuedDate = invoice.IssuedDate,
            DueDate = invoice.DueDate,
            Amount = invoice.Amount,
            ConsumptionM3 = invoice.ConsumptionM3,
            AttachmentBlobName = invoice.AttachmentBlobName,
        };
    }

    public static AdvanceResponse ToResponse(AdvancePayment payment, string? houseName = null)
    {
        return new AdvanceResponse
        {
            HouseId = payment.HouseId,
            HouseName = houseName,
            Year = payment.Year,
            Month = payment.Month,
            Amount = payment.Amount,
            PaymentDate = payment.PaymentDate,
        };
    }

    public static BillingPeriodResponse ToResponse(BillingPeriod period, decimal? totalInvoiceAmount = null)
    {
        return new BillingPeriodResponse
        {
            Id = period.Id,
            Name = period.Name,
            DateFrom = period.DateFrom,
            DateTo = period.DateTo,
            Status = period.Status.ToString(),
            TotalInvoiceAmount = totalInvoiceAmount,
        };
    }

    public static SettlementResponse ToResponse(Settlement settlement, string houseName)
    {
        return new SettlementResponse(
            PeriodId: settlement.PeriodId,
            HouseId: settlement.HouseId,
            HouseName: houseName,
            ConsumptionM3: settlement.ConsumptionM3,
            SharePercent: settlement.SharePercent,
            CalculatedAmount: settlement.CalculatedAmount,
            TotalAdvances: settlement.TotalAdvances,
            Balance: settlement.Balance,
            LossAllocatedM3: settlement.LossAllocatedM3
        );
    }

    public static DocumentResponse ToResponse(Document document)
    {
        return new DocumentResponse(
            Id: document.Id,
            Category: document.Category,
            Name: document.Name,
            FileSizeBytes: document.FileSizeBytes,
            ContentType: document.ContentType,
            UploadedAt: document.UploadedAt,
            UploadedBy: document.UploadedBy);
    }

    public static FinanceResponse ToResponse(FinancialRecord record)
    {
        return new FinanceResponse(
            Id: record.Id,
            Year: record.Year,
            Type: record.Type.ToString(),
            Category: record.Category,
            Amount: record.Amount,
            Date: record.Date,
            Description: record.Description,
            HasAttachment: !string.IsNullOrEmpty(record.AttachmentBlobName));
    }

    // IMPORTANT: Never include MagicLinkTokenHash or EntraObjectId in response
    public static UserResponse ToResponse(User user)
    {
        return new UserResponse
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role.ToString(),
            HouseId = user.HouseId,
            AuthMethod = user.AuthMethod.ToString(),
            LastLogin = user.LastLogin,
            NotificationsEnabled = user.NotificationsEnabled,
        };
    }
}
