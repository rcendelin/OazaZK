using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class CreateAdvanceRequestValidator : AbstractValidator<CreateAdvanceRequest>
{
    public CreateAdvanceRequestValidator()
    {
        RuleFor(x => x.HouseId)
            .NotEmpty().WithMessage("ID domácnosti je povinné.")
            .Must(id => Guid.TryParse(id, out _)).WithMessage("ID domácnosti musí být platné GUID.");

        RuleFor(x => x.Year)
            .InclusiveBetween(2020, 2050).WithMessage("Rok musí být mezi 2020 a 2050.");

        RuleFor(x => x.Month)
            .InclusiveBetween(1, 12).WithMessage("Měsíc musí být mezi 1 a 12.");

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
