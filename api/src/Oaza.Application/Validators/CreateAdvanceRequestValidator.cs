using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class CreateAdvanceRequestValidator : AbstractValidator<CreateAdvanceRequest>
{
    public CreateAdvanceRequestValidator()
    {
        RuleFor(x => x.HouseId)
            .NotEmpty().WithMessage("House ID is required.")
            .Must(id => Guid.TryParse(id, out _)).WithMessage("House ID must be a valid GUID.");

        RuleFor(x => x.Year)
            .InclusiveBetween(2020, 2050).WithMessage("Year must be between 2020 and 2050.");

        RuleFor(x => x.Month)
            .InclusiveBetween(1, 12).WithMessage("Month must be between 1 and 12.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount must be greater than 0.");

        RuleFor(x => x.PaymentDate)
            .LessThanOrEqualTo(DateTime.UtcNow.AddYears(1)).WithMessage("Payment date must not be in the far future.");
    }
}
