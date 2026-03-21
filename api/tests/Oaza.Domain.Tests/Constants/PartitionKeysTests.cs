using Oaza.Domain.Constants;

namespace Oaza.Domain.Tests.Constants;

public class PartitionKeysTests
{
    [Fact]
    public void PartitionKeys_ShouldHaveCorrectValues()
    {
        Assert.Equal("USER", PartitionKeys.User);
        Assert.Equal("HOUSE", PartitionKeys.House);
        Assert.Equal("METER", PartitionKeys.Meter);
        Assert.Equal("PERIOD", PartitionKeys.Period);
        Assert.Equal("INVOICE", PartitionKeys.Invoice);
    }

    [Fact]
    public void TableNames_ShouldHaveCorrectValues()
    {
        Assert.Equal("Users", TableNames.Users);
        Assert.Equal("Houses", TableNames.Houses);
        Assert.Equal("WaterMeters", TableNames.WaterMeters);
        Assert.Equal("MeterReadings", TableNames.MeterReadings);
        Assert.Equal("BillingPeriods", TableNames.BillingPeriods);
        Assert.Equal("SupplierInvoices", TableNames.SupplierInvoices);
        Assert.Equal("AdvancePayments", TableNames.AdvancePayments);
        Assert.Equal("Settlements", TableNames.Settlements);
        Assert.Equal("Documents", TableNames.Documents);
        Assert.Equal("FinancialRecords", TableNames.FinancialRecords);
    }

    [Fact]
    public void BlobContainerNames_ShouldHaveCorrectValues()
    {
        Assert.Equal("documents", BlobContainerNames.Documents);
        Assert.Equal("invoices", BlobContainerNames.Invoices);
        Assert.Equal("settlements", BlobContainerNames.Settlements);
        Assert.Equal("finance", BlobContainerNames.Finance);
    }
}
