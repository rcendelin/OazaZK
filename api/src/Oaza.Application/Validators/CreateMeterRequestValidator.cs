using FluentValidation;
using Oaza.Application.DTOs;
using Oaza.Domain.Enums;

namespace Oaza.Application.Validators;

public class CreateMeterRequestValidator : AbstractValidator<CreateMeterRequest>
{
    public CreateMeterRequestValidator()
    {
        RuleFor(x => x.MeterNumber)
            .NotEmpty().WithMessage("Meter number is required.")
            .MaximumLength(50).WithMessage("Meter number must not exceed 50 characters.");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Type is required.")
            .Must(t => Enum.TryParse<MeterType>(t, ignoreCase: true, out _))
            .WithMessage("Type must be 'Main' or 'Individual'.");

        RuleFor(x => x.HouseId)
            .Must(id => id is null || id.Length > 0)
            .WithMessage("HouseId must not be an empty string.");

        RuleFor(x => x.HouseId)
            .NotEmpty()
            .When(x => string.Equals(x.Type, "Individual", StringComparison.OrdinalIgnoreCase))
            .WithMessage("HouseId is required for Individual meters.");

        RuleFor(x => x.HouseId)
            .Must(id => Guid.TryParse(id, out _))
            .WithMessage("HouseId must be a valid GUID.")
            .When(x => x.HouseId is not null);
    }
}
