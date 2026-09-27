using FluentValidation;
using Oaza.Application.DTOs;
using Oaza.Domain.Time;

namespace Oaza.Application.Validators;

public class CreateOpeningBalanceRequestValidator : AbstractValidator<CreateOpeningBalanceRequest>
{
    public CreateOpeningBalanceRequestValidator() : this(PragueClock.System)
    {
    }

    public CreateOpeningBalanceRequestValidator(IClock clock)
    {
        RuleFor(x => x.HouseId)
            .NotEmpty().WithMessage("ID domácnosti je povinné.")
            .Must(id => Guid.TryParse(id, out _)).WithMessage("ID domácnosti musí být platné GUID.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Počáteční zůstatek musí být větší než 0.");

        RuleFor(x => x.PaymentDate)
            .LessThanOrEqualTo(PragueClock.AsUtcMidnight(clock.Today).AddYears(1)).WithMessage("Datum nesmí být příliš v budoucnosti.");

        RuleFor(x => x.Note)
            .MaximumLength(500).WithMessage("Poznámka smí mít maximálně 500 znaků.");
    }
}
