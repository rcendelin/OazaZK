using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oaza.Application.Interfaces;
using Oaza.Domain.Interfaces;
using Oaza.Infrastructure.Caching;
using Oaza.Infrastructure.Email;
using Oaza.Infrastructure.Persistence;
using Oaza.Infrastructure.Storage;

namespace Oaza.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Azure Storage clients
        var storageConnectionString = configuration["TableStorageConnection"]
            ?? configuration["AzureWebJobsStorage"]
            ?? throw new InvalidOperationException("Table Storage connection string is not configured.");

        var blobConnectionString = configuration["BlobStorageConnection"]
            ?? configuration["AzureWebJobsStorage"]
            ?? throw new InvalidOperationException("Blob Storage connection string is not configured.");

        services.AddSingleton(new TableServiceClient(storageConnectionString));
        services.AddSingleton(new BlobServiceClient(blobConnectionString));

        // Repository registrations
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IHouseRepository, HouseRepository>();
        services.AddSingleton<IWaterMeterRepository, WaterMeterRepository>();
        services.AddSingleton<IMeterReadingRepository, MeterReadingRepository>();
        services.AddSingleton<IBillingPeriodRepository, BillingPeriodRepository>();
        services.AddSingleton<ISupplierInvoiceRepository, SupplierInvoiceRepository>();
        services.AddSingleton<IAdvancePaymentRepository, AdvancePaymentRepository>();
        services.AddSingleton<ISettlementRepository, SettlementRepository>();
        services.AddSingleton<IDocumentRepository, DocumentRepository>();
        services.AddSingleton<IDocumentVersionRepository, DocumentVersionRepository>();
        services.AddSingleton<IFinancialRecordRepository, FinancialRecordRepository>();

        // Import session cache
        services.AddSingleton<IImportSessionCache, InMemoryImportSessionCache>();

        // Blob Storage service
        services.AddSingleton<IBlobStorageService, BlobStorageService>();

        // Email service (Azure Communication Services)
        // Azure Functions maps env var double-underscore (__) to colon (:) in configuration
        services.Configure<AcsSettings>(options =>
        {
            options.ConnectionString = configuration["AzureCommunicationServices:ConnectionString"]
                ?? configuration["AzureCommunicationServices__ConnectionString"]
                ?? string.Empty;
            options.FromEmail = configuration["AzureCommunicationServices:FromEmail"]
                ?? configuration["AzureCommunicationServices__FromEmail"]
                ?? string.Empty;
            options.FromName = configuration["AzureCommunicationServices:FromName"]
                ?? configuration["AzureCommunicationServices__FromName"]
                ?? string.Empty;
        });
        services.AddSingleton<IEmailService, AcsEmailService>();

        // Notification service
        services.AddSingleton<INotificationService, NotificationService>();

        return services;
    }
}
