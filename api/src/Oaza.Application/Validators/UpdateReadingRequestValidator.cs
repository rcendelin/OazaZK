using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class UpdateReadingRequestValidator : AbstractValidator<UpdateReadingRequest>
{
    public UpdateReadingRequestValidator()
    {
        RuleFor(x => x.Value)
            .GreaterThanOrEqualTo(0).WithMessage("Hodnota musí být větší nebo rovna 0.");
    }
}
