namespace Oaza.Domain.Constants;

public static class AuditActions
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";

    /// <summary>Correction record for data that can no longer be edited (e.g. after an interim closing).</summary>
    public const string Correction = "Correction";
}
