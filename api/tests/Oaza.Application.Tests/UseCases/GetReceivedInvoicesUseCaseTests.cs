using FluentAssertions;
using Moq;
using Oaza.Application.UseCases;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.UseCases;

public class GetReceivedInvoicesUseCaseTests
{
    private readonly Mock<ISupplierInvoiceRepository> _invoiceRepo = new();
    private readonly Mock<IFinancialRecordRepository> _financialRecordRepo = new();
    private readonly GetReceivedInvoicesUseCase _sut;

    public GetReceivedInvoicesUseCaseTests()
    {
        _sut = new GetReceivedInvoicesUseCase(_invoiceRepo.Object, _financialRecordRepo.Object);
    }

    private void SetupInvoices(params SupplierInvoice[] invoices) =>
        _invoiceRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Invoice)).ReturnsAsync(invoices.ToList());

    private void SetupRecords(params FinancialRecord[] records) =>
        _financialRecordRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(records.ToList());

    [Fact]
    public async Task GetAsync_NoFilters_JoinsBothSources_SortedByDateDescending()
    {
        SetupInvoices(new SupplierInvoice
        {
            Id = "inv-1", InvoiceNumber = "F2026-1",
            IssuedDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            Amount = 1000m, AttachmentBlobName = "inv-1/faktura.pdf",
        });
        SetupRecords(new FinancialRecord
        {
            Id = "rec-1", Type = FinancialRecordType.Expense, Category = "elektro",
            Amount = 500m, Date = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Description = "Elektřina",
        });

        var result = await _sut.GetAsync(null, null);

        result.Should().HaveCount(2);
        // Newest first: elektro (June) before voda (March)
        result[0].Source.Should().Be("ostatni");
        result[0].Id.Should().Be("rec-1");
        result[1].Source.Should().Be("voda");
        result[1].Id.Should().Be("inv-1");
    }

    [Fact]
    public async Task GetAsync_MapsWaterInvoiceFields()
    {
        SetupInvoices(new SupplierInvoice
        {
            Id = "inv-1", InvoiceNumber = "F2026-1",
            IssuedDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            Amount = 1234m, AttachmentBlobName = "inv-1/faktura.pdf",
        });
        SetupRecords();

        var water = (await _sut.GetAsync(null, null)).Single();

        water.Source.Should().Be("voda");
        water.Category.Should().Be("voda");
        water.Description.Should().Be("F2026-1");
        water.Amount.Should().Be(1234m);
        water.DueDate.Should().Be(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc));
        water.CountsTowardWaterSettlement.Should().BeTrue();
        water.HasAttachment.Should().BeTrue();
        water.AttachmentDownloadPath.Should().Be("/invoices/inv-1/attachment");
    }

    [Fact]
    public async Task GetAsync_MapsFinancialRecordFields_NoAttachment()
    {
        SetupInvoices();
        SetupRecords(new FinancialRecord
        {
            Id = "rec-1", Type = FinancialRecordType.Expense, Category = "pojisteni",
            Amount = 800m, Date = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Description = "Pojištění", AttachmentBlobName = null,
        });

        var rec = (await _sut.GetAsync(null, null)).Single();

        rec.Source.Should().Be("ostatni");
        rec.Category.Should().Be("pojisteni");
        rec.Description.Should().Be("Pojištění");
        rec.Amount.Should().Be(800m);
        rec.DueDate.Should().BeNull();
        rec.CountsTowardWaterSettlement.Should().BeFalse();
        rec.HasAttachment.Should().BeFalse();
        rec.AttachmentDownloadPath.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_FiltersByYear_WaterByIssuedDate_RecordByDate()
    {
        SetupInvoices(
            new SupplierInvoice { Id = "inv-2025", InvoiceNumber = "A", IssuedDate = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc), Amount = 1m },
            new SupplierInvoice { Id = "inv-2026", InvoiceNumber = "B", IssuedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Amount = 2m });
        SetupRecords(
            new FinancialRecord { Id = "rec-2025", Type = FinancialRecordType.Expense, Category = "elektro", Amount = 3m, Date = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), Description = "x" },
            new FinancialRecord { Id = "rec-2026", Type = FinancialRecordType.Expense, Category = "elektro", Amount = 4m, Date = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), Description = "y" });

        var result = await _sut.GetAsync(2026, null);

        result.Select(x => x.Id).Should().BeEquivalentTo(new[] { "inv-2026", "rec-2026" });
    }

    [Fact]
    public async Task GetAsync_CategoryVoda_ReturnsOnlyWater()
    {
        SetupInvoices(new SupplierInvoice { Id = "inv-1", InvoiceNumber = "A", IssuedDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Amount = 1m });
        SetupRecords(
            new FinancialRecord { Id = "rec-voda", Type = FinancialRecordType.Expense, Category = "voda", Amount = 2m, Date = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Description = "x" },
            new FinancialRecord { Id = "rec-elektro", Type = FinancialRecordType.Expense, Category = "elektro", Amount = 3m, Date = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Description = "y" });

        var result = await _sut.GetAsync(null, "voda");

        // Only the SupplierInvoice — a FinancialRecord miscategorized as "voda" is NOT pulled in.
        result.Select(x => x.Id).Should().BeEquivalentTo(new[] { "inv-1" });
    }

    [Fact]
    public async Task GetAsync_CategoryOther_ReturnsOnlyThatCategoryRecords()
    {
        SetupInvoices(new SupplierInvoice { Id = "inv-1", InvoiceNumber = "A", IssuedDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Amount = 1m });
        SetupRecords(
            new FinancialRecord { Id = "rec-elektro", Type = FinancialRecordType.Expense, Category = "elektro", Amount = 2m, Date = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Description = "x" },
            new FinancialRecord { Id = "rec-udrzba", Type = FinancialRecordType.Expense, Category = "udrzba", Amount = 3m, Date = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Description = "y" });

        var result = await _sut.GetAsync(null, "elektro");

        result.Select(x => x.Id).Should().BeEquivalentTo(new[] { "rec-elektro" });
    }

    [Fact]
    public async Task GetAsync_IgnoresIncomeRecords()
    {
        SetupInvoices();
        SetupRecords(
            new FinancialRecord { Id = "expense", Type = FinancialRecordType.Expense, Category = "elektro", Amount = 1m, Date = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Description = "x" },
            new FinancialRecord { Id = "income", Type = FinancialRecordType.Income, Category = "jine", Amount = 2m, Date = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Description = "y" });

        var result = await _sut.GetAsync(null, null);

        result.Select(x => x.Id).Should().BeEquivalentTo(new[] { "expense" });
    }
}
