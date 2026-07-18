using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class CreateBillingPeriodRequestValidator : AbstractValidator<CreateBillingPeriodRequest>
{
    public CreateBillingPeriodRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Název je povinný.")
            .MaximumLength(100).WithMessage("Název nesmí přesáhnout 100 znaků.");

        RuleFor(x => x.DateFrom)
            .LessThan(x => x.DateTo).WithMessage("Datum od musí být před datem do.");

        RuleFor(x => x.DateTo)
            .LessThanOrEqualTo(DateTime.UtcNow.AddYears(2)).WithMessage("Datum do nesmí být příliš v budoucnosti.");
    }
}
