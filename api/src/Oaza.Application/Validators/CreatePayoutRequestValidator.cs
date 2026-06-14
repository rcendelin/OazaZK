using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class CreatePayoutRequestValidator : AbstractValidator<CreatePayoutRequest>
{
    public CreatePayoutRequestValidator()
    {
        RuleFor(x => x.HouseId)
            .NotEmpty().WithMessage("House ID is required.")
            .Must(id => Guid.TryParse(id, out _)).WithMessage("House ID must be a valid GUID.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Payout amount must be greater than 0.");

        RuleFor(x => x.PaymentDate)
            .LessThanOrEqualTo(DateTime.UtcNow.AddYears(1)).WithMessage("Date must not be in the far future.");

        RuleFor(x => x.Note)
            .MaximumLength(500).WithMessage("Note must be 500 characters or fewer.");
    }
}
