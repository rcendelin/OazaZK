using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class ConfirmImportRequestValidator : AbstractValidator<ConfirmImportRequest>
{
    public const int MaxReadings = 5000;

    public ConfirmImportRequestValidator()
    {
        RuleFor(x => x.Readings)
            .NotEmpty().WithMessage("Nejsou žádné odečty k importu.")
            .Must(r => r.Count <= MaxReadings).WithMessage($"Najednou lze importovat nejvýš {MaxReadings} odečtů.");

        RuleForEach(x => x.Readings).ChildRules(reading =>
        {
            reading.RuleFor(r => r.MeterId).NotEmpty().WithMessage("Chybí vodoměr.");
            reading.RuleFor(r => r.Value).GreaterThanOrEqualTo(0).WithMessage("Stav vodoměru nesmí být záporný.");
            reading.RuleFor(r => r.ReadingDate).NotEqual(default(DateTime)).WithMessage("Chybí datum odečtu.");
        });
    }
}
