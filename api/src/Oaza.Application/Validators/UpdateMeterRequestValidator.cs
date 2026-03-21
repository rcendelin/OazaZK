using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class UpdateMeterRequestValidator : AbstractValidator<UpdateMeterRequest>
{
    public UpdateMeterRequestValidator()
    {
        RuleFor(x => x.MeterNumber)
            .NotEmpty().WithMessage("Meter number is required.")
            .MaximumLength(50).WithMessage("Meter number must not exceed 50 characters.");

        RuleFor(x => x.HouseId)
            .Must(id => id is null || id.Length > 0)
            .WithMessage("HouseId must not be an empty string.");

        RuleFor(x => x.HouseId)
            .Must(id => Guid.TryParse(id, out _))
            .WithMessage("HouseId must be a valid GUID.")
            .When(x => x.HouseId is not null);
    }
}
