using FluentValidation;
using Oaza.Application.DTOs;
using Oaza.Domain.Time;

namespace Oaza.Application.Validators;

public class UpdateAdvanceRequestValidator : AbstractValidator<UpdateAdvanceRequest>
{
    public UpdateAdvanceRequestValidator() : this(PragueClock.System)
    {
    }

    public UpdateAdvanceRequestValidator(IClock clock)
    {
        RuleFor(x => x.WaterAmount).GreaterThanOrEqualTo(0).WithMessage("Částka za vodu nesmí být záporná.");
        RuleFor(x => x.ElectricityAmount).GreaterThanOrEqualTo(0).WithMessage("Částka za elektřinu nesmí být záporná.");
        RuleFor(x => x.CommonAmount).GreaterThanOrEqualTo(0).WithMessage("Částka za společné výdaje nesmí být záporná.");

        RuleFor(x => x)
            .Must(x => x.WaterAmount + x.ElectricityAmount + x.CommonAmount > 0)
            .WithMessage("Celková částka musí být větší než 0.");

        RuleFor(x => x.PaymentDate)
            .LessThanOrEqualTo(PragueClock.AsUtcMidnight(clock.Today).AddYears(1)).WithMessage("Datum platby nesmí být příliš v budoucnosti.");
    }
}
