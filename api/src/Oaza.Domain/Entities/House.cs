namespace Oaza.Domain.Entities;

public class House
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When true, the household is dissolving its overpayment (přeplatek) instead
    /// of sending regular payments — the credit is consumed by ongoing charges.
    /// Operational flag only; does not change the saldo math.
    /// </summary>
    public bool DissolveOverpayment { get; set; }
}
