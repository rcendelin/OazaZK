using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class CreateBillingPeriodRequestValidator : AbstractValidator<CreateBillingPeriodRequest>
{
    public CreateBillingPeriodRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters.");

        RuleFor(x => x.DateFrom)
            .LessThan(x => x.DateTo).WithMessage("DateFrom must be before DateTo.");

        RuleFor(x => x.DateTo)
            .LessThanOrEqualTo(DateTime.UtcNow.AddYears(2)).WithMessage("DateTo must not be in the far future.");
    }
}
