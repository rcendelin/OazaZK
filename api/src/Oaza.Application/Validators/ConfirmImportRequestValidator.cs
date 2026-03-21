using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class ConfirmImportRequestValidator : AbstractValidator<ConfirmImportRequest>
{
    public ConfirmImportRequestValidator()
    {
        RuleFor(x => x.ImportSessionId)
            .NotEmpty().WithMessage("ImportSessionId is required.")
            .Must(id => Guid.TryParse(id, out _)).WithMessage("ImportSessionId must be a valid GUID.");
    }
}
