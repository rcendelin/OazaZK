using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class CreateDoplatekRequestValidator : AbstractValidator<CreateDoplatekRequest>
{
    public CreateDoplatekRequestValidator()
    {
        RuleFor(x => x.HouseId)
            .NotEmpty().WithMessage("House ID is required.")
            .Must(id => Guid.TryParse(id, out _)).WithMessage("House ID must be a valid GUID.");

        RuleFor(x => x.WaterAmount).GreaterThanOrEqualTo(0).WithMessage("Water amount cannot be negative.");
        RuleFor(x => x.ElectricityAmount).GreaterThanOrEqualTo(0).WithMessage("Electricity amount cannot be negative.");
        RuleFor(x => x.CommonAmount).GreaterThanOrEqualTo(0).WithMessage("Common amount cannot be negative.");

        RuleFor(x => x)
            .Must(x => x.WaterAmount + x.ElectricityAmount + x.CommonAmount > 0)
            .WithMessage("Total amount must be greater than 0.");

        RuleFor(x => x.PaymentDate)
            .LessThanOrEqualTo(DateTime.UtcNow.AddYears(1)).WithMessage("Payment date must not be in the far future.");

        RuleFor(x => x.Note)
            .MaximumLength(500).WithMessage("Note must be 500 characters or fewer.");
    }
}
