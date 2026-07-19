using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class UpdateMeterRequestValidator : AbstractValidator<UpdateMeterRequest>
{
    public UpdateMeterRequestValidator()
    {
        RuleFor(x => x.MeterNumber)
            .NotEmpty().WithMessage("Číslo vodoměru je povinné.")
            .MaximumLength(50).WithMessage("Číslo vodoměru nesmí přesáhnout 50 znaků.");

        RuleFor(x => x.HouseId)
            .Must(id => id is null || id.Length > 0)
            .WithMessage("HouseId nesmí být prázdný řetězec.");

        RuleFor(x => x.HouseId)
            .Must(id => Guid.TryParse(id, out _))
            .WithMessage("HouseId musí být platné GUID.")
            .When(x => x.HouseId is not null);
    }
}
