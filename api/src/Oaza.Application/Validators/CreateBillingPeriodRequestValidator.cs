using FluentValidation;
using Oaza.Application.DTOs;
using Oaza.Domain.Time;

namespace Oaza.Application.Validators;

public class CreateBillingPeriodRequestValidator : AbstractValidator<CreateBillingPeriodRequest>
{
    public CreateBillingPeriodRequestValidator() : this(PragueClock.System)
    {
    }

    public CreateBillingPeriodRequestValidator(IClock clock)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Název je povinný.")
            .MaximumLength(100).WithMessage("Název nesmí přesáhnout 100 znaků.");

        RuleFor(x => x.DateFrom)
            .LessThan(x => x.DateTo).WithMessage("Datum od musí být před datem do.");

        RuleFor(x => x.DateTo)
            .LessThanOrEqualTo(PragueClock.AsUtcMidnight(clock.Today).AddYears(2)).WithMessage("Datum do nesmí být příliš v budoucnosti.");
    }
}
