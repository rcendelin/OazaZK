using FluentValidation;
using Oaza.Application.DTOs;
using Oaza.Domain.Enums;

namespace Oaza.Application.Validators;

public class CreateMeterRequestValidator : AbstractValidator<CreateMeterRequest>
{
    public CreateMeterRequestValidator()
    {
        RuleFor(x => x.MeterNumber)
            .NotEmpty().WithMessage("Číslo vodoměru je povinné.")
            .MaximumLength(50).WithMessage("Číslo vodoměru nesmí přesáhnout 50 znaků.");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Typ je povinný.")
            .Must(t => Enum.TryParse<MeterType>(t, ignoreCase: true, out _))
            .WithMessage("Typ musí být 'Main' nebo 'Individual'.");

        RuleFor(x => x.HouseId)
            .Must(id => id is null || id.Length > 0)
            .WithMessage("HouseId nesmí být prázdný řetězec.");

        RuleFor(x => x.HouseId)
            .NotEmpty()
            .When(x => string.Equals(x.Type, "Individual", StringComparison.OrdinalIgnoreCase))
            .WithMessage("U individuálních vodoměrů je HouseId povinné.");

        RuleFor(x => x.HouseId)
            .Must(id => Guid.TryParse(id, out _))
            .WithMessage("HouseId musí být platné GUID.")
            .When(x => x.HouseId is not null);
    }
}
