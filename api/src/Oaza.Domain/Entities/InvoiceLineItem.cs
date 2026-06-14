namespace Oaza.Domain.Entities;

/// <summary>
/// A single sub-reading line on a supplier invoice (one invoice can have many).
/// Amounts are stored excluding VAT, as they appear on the supplier's invoice.
/// </summary>
public class InvoiceLineItem
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public decimal StartReading { get; set; }    // m³ (počáteční stav)
    public decimal EndReading { get; set; }      // m³ (konečný stav)
    public decimal ConsumptionM3 { get; set; }   // m³ (fakturované množství)
    public decimal UnitPrice { get; set; }       // Kč/m³ bez DPH
    public decimal AmountExclVat { get; set; }   // cena řádku bez DPH (Kč)
}
