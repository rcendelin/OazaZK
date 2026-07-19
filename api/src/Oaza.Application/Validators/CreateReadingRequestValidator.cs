using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class CreateReadingRequestValidator : AbstractValidator<CreateReadingRequest>
{
    public CreateReadingRequestValidator()
    {
        RuleFor(x => x.MeterId)
            .NotEmpty().WithMessage("MeterId je povinné.")
            .Must(id => Guid.TryParse(id, out _)).WithMessage("MeterId musí být platné GUID.");

        RuleFor(x => x.ReadingDate)
            .NotEmpty().WithMessage("Datum odečtu je povinné.")
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1)).WithMessage("Datum odečtu nesmí být v budoucnosti.");

        RuleFor(x => x.Value)
            .GreaterThanOrEqualTo(0).WithMessage("Hodnota musí být větší nebo rovna 0.");
    }
}
