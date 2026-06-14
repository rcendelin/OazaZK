using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class UpdateAdvanceRequestValidator : AbstractValidator<UpdateAdvanceRequest>
{
    public UpdateAdvanceRequestValidator()
    {
        RuleFor(x => x.WaterAmount).GreaterThanOrEqualTo(0).WithMessage("Water amount cannot be negative.");
        RuleFor(x => x.ElectricityAmount).GreaterThanOrEqualTo(0).WithMessage("Electricity amount cannot be negative.");
        RuleFor(x => x.CommonAmount).GreaterThanOrEqualTo(0).WithMessage("Common amount cannot be negative.");

        RuleFor(x => x)
            .Must(x => x.WaterAmount + x.ElectricityAmount + x.CommonAmount > 0)
            .WithMessage("Total amount must be greater than 0.");

        RuleFor(x => x.PaymentDate)
            .LessThanOrEqualTo(DateTime.UtcNow.AddYears(1)).WithMessage("Payment date must not be in the far future.");
    }
}
