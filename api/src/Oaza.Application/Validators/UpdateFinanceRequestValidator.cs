using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class UpdateFinanceRequestValidator : AbstractValidator<UpdateFinanceRequest>
{
    private static readonly string[] AllowedTypes = { "Income", "Expense" };
    private static readonly string[] AllowedCategories = { "voda", "elektro", "udrzba", "pojisteni", "jine", "fond-voda" };

    public UpdateFinanceRequestValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Typ je povinný.")
            .Must(t => AllowedTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Typ musí být jeden z: {string.Join(", ", AllowedTypes)}.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Kategorie je povinná.")
            .Must(c => AllowedCategories.Contains(c, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Kategorie musí být jedna z: {string.Join(", ", AllowedCategories)}.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Částka musí být větší než 0.");

        RuleFor(x => x.Date)
            .LessThanOrEqualTo(DateTime.UtcNow.AddYears(1)).WithMessage("Datum nesmí být příliš v budoucnosti.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Popis je povinný.")
            .MaximumLength(500).WithMessage("Popis nesmí přesáhnout 500 znaků.");
    }
}
