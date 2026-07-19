using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class UpdateAdvanceRequestValidator : AbstractValidator<UpdateAdvanceRequest>
{
    public UpdateAdvanceRequestValidator()
    {
        RuleFor(x => x.WaterAmount).GreaterThanOrEqualTo(0).WithMessage("Částka za vodu nesmí být záporná.");
        RuleFor(x => x.ElectricityAmount).GreaterThanOrEqualTo(0).WithMessage("Částka za elektřinu nesmí být záporná.");
        RuleFor(x => x.CommonAmount).GreaterThanOrEqualTo(0).WithMessage("Částka za společné výdaje nesmí být záporná.");

        RuleFor(x => x)
            .Must(x => x.WaterAmount + x.ElectricityAmount + x.CommonAmount > 0)
            .WithMessage("Celková částka musí být větší než 0.");

        RuleFor(x => x.PaymentDate)
            .LessThanOrEqualTo(DateTime.UtcNow.AddYears(1)).WithMessage("Datum platby nesmí být příliš v budoucnosti.");
    }
}
