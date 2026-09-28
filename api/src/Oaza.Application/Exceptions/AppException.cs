namespace Oaza.Application.Exceptions;

public class AppException : Exception
{
    public int StatusCode { get; }

    public AppException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }
}

public class NotFoundException : AppException
{
    public NotFoundException(string entity, string id)
        : base("Požadovaný záznam nebyl nalezen.", 404) { }
}

/// <summary>A business rule rejected the change; <see cref="Errors"/> holds every reason (Czech, user-facing).</summary>
public class BusinessRuleException : AppException
{
    public IReadOnlyList<string> Errors { get; }

    public BusinessRuleException(IReadOnlyList<string> errors)
        : base(errors.Count == 1 ? errors[0] : "Změnu nelze uložit.", 400)
    {
        Errors = errors;
    }
}
