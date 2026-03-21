using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class UpdateFinanceRequestValidator : AbstractValidator<UpdateFinanceRequest>
{
    private static readonly string[] AllowedTypes = { "Income", "Expense" };
    private static readonly string[] AllowedCategories = { "voda", "elektro", "udrzba", "pojisteni", "jine" };

    public UpdateFinanceRequestValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Type is required.")
            .Must(t => AllowedTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Type must be one of: {string.Join(", ", AllowedTypes)}.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required.")
            .Must(c => AllowedCategories.Contains(c, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Category must be one of: {string.Join(", ", AllowedCategories)}.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount must be greater than 0.");

        RuleFor(x => x.Date)
            .LessThanOrEqualTo(DateTime.UtcNow.AddYears(1)).WithMessage("Date must not be in the far future.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.")
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters.");
    }
}
