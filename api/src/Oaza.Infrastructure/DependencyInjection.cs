using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oaza.Domain.Interfaces;
using Oaza.Infrastructure.Persistence;

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
        services.AddSingleton<IFinancialRecordRepository, FinancialRecordRepository>();

        return services;
    }
}
