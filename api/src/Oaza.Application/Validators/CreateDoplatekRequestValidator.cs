using FluentValidation;
using Oaza.Application.DTOs;
using Oaza.Domain.Time;

namespace Oaza.Application.Validators;

public class CreateDoplatekRequestValidator : AbstractValidator<CreateDoplatekRequest>
{
    public CreateDoplatekRequestValidator() : this(PragueClock.System)
    {
    }

    public CreateDoplatekRequestValidator(IClock clock)
    {
        RuleFor(x => x.HouseId)
            .NotEmpty().WithMessage("ID domácnosti je povinné.")
            .Must(id => Guid.TryParse(id, out _)).WithMessage("ID domácnosti musí být platné GUID.");

        RuleFor(x => x.WaterAmount).GreaterThanOrEqualTo(0).WithMessage("Částka za vodu nesmí být záporná.");
        RuleFor(x => x.ElectricityAmount).GreaterThanOrEqualTo(0).WithMessage("Částka za elektřinu nesmí být záporná.");
        RuleFor(x => x.CommonAmount).GreaterThanOrEqualTo(0).WithMessage("Částka za společné výdaje nesmí být záporná.");

        RuleFor(x => x)
            .Must(x => x.WaterAmount + x.ElectricityAmount + x.CommonAmount > 0)
            .WithMessage("Celková částka musí být větší než 0.");

        RuleFor(x => x.PaymentDate)
            .LessThanOrEqualTo(PragueClock.AsUtcMidnight(clock.Today).AddYears(1)).WithMessage("Datum platby nesmí být příliš v budoucnosti.");

        RuleFor(x => x.Note)
            .MaximumLength(500).WithMessage("Poznámka smí mít maximálně 500 znaků.");
    }
}
