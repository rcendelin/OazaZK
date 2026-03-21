using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class UpdateInvoiceRequestValidator : AbstractValidator<UpdateInvoiceRequest>
{
    public UpdateInvoiceRequestValidator()
    {
        RuleFor(x => x.InvoiceNumber)
            .NotEmpty().WithMessage("Invoice number is required.")
            .MaximumLength(50).WithMessage("Invoice number must not exceed 50 characters.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount must be greater than 0.");

        RuleFor(x => x.ConsumptionM3)
            .GreaterThanOrEqualTo(0).WithMessage("Consumption must be greater than or equal to 0.");

        RuleFor(x => x.IssuedDate)
            .LessThanOrEqualTo(DateTime.UtcNow.AddYears(1)).WithMessage("Issued date must not be in the far future.");

        RuleFor(x => x.DueDate)
            .LessThanOrEqualTo(DateTime.UtcNow.AddYears(1)).WithMessage("Due date must not be in the far future.");
    }
}
