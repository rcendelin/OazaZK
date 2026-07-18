using Oaza.Application.DTOs;
using Oaza.Domain.Constants;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.UseCases;

/// <summary>
/// Builds a unified, read-only overview of all received invoices by joining
/// water <see cref="Oaza.Domain.Entities.SupplierInvoice"/> records with expense
/// <see cref="Oaza.Domain.Entities.FinancialRecord"/> records. Never mutates data
/// and never feeds into the water settlement calculation — the two entities stay
/// structurally separate (see spec §9).
/// </summary>
public class GetReceivedInvoicesUseCase
{
    private readonly ISupplierInvoiceRepository _invoiceRepository;
    private readonly IFinancialRecordRepository _financialRecordRepository;

    public GetReceivedInvoicesUseCase(
        ISupplierInvoiceRepository invoiceRepository,
        IFinancialRecordRepository financialRecordRepository)
    {
        _invoiceRepository = invoiceRepository;
        _financialRecordRepository = financialRecordRepository;
    }

    /// <param name="year">Calendar year of the document date; null = all years.</param>
    /// <param name="category">
    /// null/empty = both groups; "voda" = only water SupplierInvoices;
    /// any other value = only FinancialRecords of that category.
    /// </param>
    public async Task<IReadOnlyList<ReceivedInvoiceResponse>> GetAsync(int? year, string? category)
    {
        var isWaterFilter = !string.IsNullOrEmpty(category)
            && category.Equals("voda", StringComparison.OrdinalIgnoreCase);
        var wantWater = string.IsNullOrEmpty(category) || isWaterFilter;
        var wantOther = string.IsNullOrEmpty(category) || !isWaterFilter;

        var result = new List<ReceivedInvoiceResponse>();

        if (wantWater)
        {
            var invoices = await _invoiceRepository.GetByPartitionKeyAsync(PartitionKeys.Invoice);
            foreach (var inv in invoices.Where(i => year == null || i.IssuedDate.Year == year))
            {
                var hasAttachment = !string.IsNullOrEmpty(inv.AttachmentBlobName);
                result.Add(new ReceivedInvoiceResponse(
                    Source: "voda",
                    Id: inv.Id,
                    Date: inv.IssuedDate,
                    Category: "voda",
                    Description: inv.InvoiceNumber,
                    Amount: inv.Amount,
                    DueDate: inv.DueDate,
                    CountsTowardWaterSettlement: true,
                    HasAttachment: hasAttachment,
                    AttachmentDownloadPath: hasAttachment ? $"/invoices/{inv.Id}/attachment" : null));
            }
        }

        if (wantOther)
        {
            var records = await _financialRecordRepository.GetAllAsync();
            foreach (var r in records.Where(r =>
                r.Type == FinancialRecordType.Expense
                && (year == null || r.Date.Year == year)
                && (string.IsNullOrEmpty(category)
                    || r.Category.Equals(category, StringComparison.OrdinalIgnoreCase))))
            {
                var hasAttachment = !string.IsNullOrEmpty(r.AttachmentBlobName);
                result.Add(new ReceivedInvoiceResponse(
                    Source: "ostatni",
                    Id: r.Id,
                    Date: r.Date,
                    Category: r.Category,
                    Description: r.Description,
                    Amount: r.Amount,
                    DueDate: null,
                    CountsTowardWaterSettlement: false,
                    HasAttachment: hasAttachment,
                    AttachmentDownloadPath: hasAttachment ? $"/finance/{r.Id}/attachment" : null));
            }
        }

        return result.OrderByDescending(x => x.Date).ToList();
    }
}
