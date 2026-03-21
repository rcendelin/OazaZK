using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class UpdateAdvanceRequestValidator : AbstractValidator<UpdateAdvanceRequest>
{
    public UpdateAdvanceRequestValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount must be greater than 0.");

        RuleFor(x => x.PaymentDate)
            .LessThanOrEqualTo(DateTime.UtcNow.AddYears(1)).WithMessage("Payment date must not be in the far future.");
    }
}
