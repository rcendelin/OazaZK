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
            DissolveOverpayment = house.DissolveOverpayment,
        };
    }

    public static MeterResponse ToResponse(WaterMeter meter, string? houseName = null)
    {
        return new MeterResponse
        {
            Id = meter.Id,
            MeterNumber = meter.MeterNumber,
            Name = meter.Name,
            Type = meter.Type.ToString(),
            HouseId = meter.HouseId,
            HouseName = houseName,
            RadioAddress = meter.RadioAddress,
            InstallationDate = meter.InstallationDate,
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
            WaterAmount = payment.WaterAmount,
            ElectricityAmount = payment.ElectricityAmount,
            CommonAmount = payment.CommonAmount,
            PaymentDate = payment.PaymentDate,
            Type = payment.Type.ToString(),
            Note = payment.Note,
            IsFundTransfer = payment.IsFundTransfer,
            IsFromBank = payment.BankTransactionId is not null,
            RowKey = payment.RowKey,
        };
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
            UploadedBy: document.UploadedBy,
            ComponentId: document.ComponentId);
    }

    public static DocumentVersionResponse ToResponse(DocumentVersion version)
    {
        return new DocumentVersionResponse(
            VersionNumber: version.VersionNumber,
            FileSizeBytes: version.FileSizeBytes,
            ContentType: version.ContentType,
            UploadedAt: version.UploadedAt,
            UploadedBy: version.UploadedBy);
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
