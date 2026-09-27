using System.Text.RegularExpressions;

namespace Oaza.Domain.Helpers;

/// <summary>
/// Czech domestic account number: optional prefix, number and 4-digit bank code
/// ("107-2222222222/0100"). Normalizes different spellings of the same account
/// (spaces, leading zeros, zero prefix) to one comparable form.
/// </summary>
public sealed partial record BankAccountNumber(string Prefix, string Number, string BankCode)
{
    [GeneratedRegex(@"^(?:(\d{1,16})-)?(\d{1,20})/(\d{4})$")]
    private static partial Regex FullPattern();

    /// <summary>Builds an account from the separate CSV columns (account incl. optional prefix, bank code).</summary>
    public static BankAccountNumber? FromParts(string? account, string? bankCode)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(bankCode)) return null;
        return TryParse($"{account.Trim()}/{bankCode.Trim()}", out var result) ? result : null;
    }

    /// <summary>Parses "[prefix-]number/bank" (whitespace ignored).</summary>
    public static bool TryParse(string? text, out BankAccountNumber? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = FullPattern().Match(Regex.Replace(text, @"\s", string.Empty));
        if (!match.Success) return false;

        var prefix = match.Groups[1].Value.TrimStart('0');
        var number = match.Groups[2].Value.TrimStart('0');
        // Zero-padded forms (e.g. GPC's 16-digit field) are fine; the significant part must fit the Czech format.
        if (number.Length is < 2 or > 10 || prefix.Length > 6) return false;

        result = new BankAccountNumber(prefix, number, match.Groups[3].Value);
        return true;
    }

    /// <summary>Storage-safe key ("107-2222222222_0100"); Table Storage forbids '/' in keys.</summary>
    public string ToKey() => $"{AccountPart}_{BankCode}";

    /// <summary>Display form ("107-2222222222/0100").</summary>
    public override string ToString() => $"{AccountPart}/{BankCode}";

    /// <summary>Converts a key produced by <see cref="ToKey"/> back to the display form.</summary>
    public static string KeyToDisplay(string key) => key.Replace('_', '/');

    private string AccountPart => Prefix.Length > 0 ? $"{Prefix}-{Number}" : Number;
}
