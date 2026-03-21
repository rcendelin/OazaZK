using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class UpdateReadingRequestValidator : AbstractValidator<UpdateReadingRequest>
{
    public UpdateReadingRequestValidator()
    {
        RuleFor(x => x.Value)
            .GreaterThanOrEqualTo(0).WithMessage("Value must be greater than or equal to 0.");
    }
}
