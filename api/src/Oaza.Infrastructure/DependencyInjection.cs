using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oaza.Application.Interfaces;
using Oaza.Domain.Interfaces;
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

        var tableClient = new TableServiceClient(storageConnectionString);
        var blobClient = new BlobServiceClient(blobConnectionString);

        // T01: test and prod never share data — refuse to start on another environment's storage account.
        var environment = configuration[Oaza.Application.Deployment.DeploymentEnvironment.ConfigKey] ?? string.Empty;
        var violation = Oaza.Application.Deployment.StorageIsolation.Violation(environment, tableClient.AccountName)
            ?? Oaza.Application.Deployment.StorageIsolation.Violation(environment, blobClient.AccountName);
        if (violation is not null)
            throw new InvalidOperationException(violation);

        services.AddSingleton(tableClient);
        services.AddSingleton(blobClient);

        // Repository registrations
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IHouseRepository, HouseRepository>();
        services.AddSingleton<IWaterMeterRepository, WaterMeterRepository>();
        services.AddSingleton<IMeterReadingRepository, MeterReadingRepository>();
        services.AddSingleton<IAdvancePaymentRepository, AdvancePaymentRepository>();
        services.AddSingleton<IAdvanceSettingsRepository, AdvanceSettingsRepository>();
        services.AddSingleton<IDocumentRepository, DocumentRepository>();
        services.AddSingleton<IDocumentVersionRepository, DocumentVersionRepository>();
        services.AddSingleton<IFinancialRecordRepository, FinancialRecordRepository>();
        services.AddSingleton<IBankAccountMappingRepository, BankAccountMappingRepository>();
        services.AddSingleton<IBankTransactionRepository, BankTransactionRepository>();
        services.AddSingleton<IAuditLogRepository, AuditLogRepository>();
        services.AddSingleton<ICostComponentRepository, CostComponentRepository>();
        services.AddSingleton<IComponentAllocationRuleRepository, ComponentAllocationRuleRepository>();
        services.AddSingleton<IParticipationRepository, ParticipationRepository>();
        services.AddSingleton<IOwnershipPeriodRepository, OwnershipPeriodRepository>();
        services.AddSingleton<IOpeningBalanceRepository, OpeningBalanceRepository>();
        services.AddSingleton<ICostEntryRepository, CostEntryRepository>();
        services.AddSingleton<IInterimClosingRepository, InterimClosingRepository>();
        services.AddSingleton<ICashBookRepository, CashBookRepository>();
        services.AddSingleton<IOffBookFundRepository, OffBookFundRepository>();

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
