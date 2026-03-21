using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class CreateReadingRequestValidator : AbstractValidator<CreateReadingRequest>
{
    public CreateReadingRequestValidator()
    {
        RuleFor(x => x.MeterId)
            .NotEmpty().WithMessage("MeterId is required.")
            .Must(id => Guid.TryParse(id, out _)).WithMessage("MeterId must be a valid GUID.");

        RuleFor(x => x.ReadingDate)
            .NotEmpty().WithMessage("ReadingDate is required.")
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1)).WithMessage("ReadingDate cannot be in the future.");

        RuleFor(x => x.Value)
            .GreaterThanOrEqualTo(0).WithMessage("Value must be greater than or equal to 0.");
    }
}
