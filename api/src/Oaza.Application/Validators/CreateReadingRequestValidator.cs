using FluentValidation;
using Oaza.Application.DTOs;
using Oaza.Domain.Time;

namespace Oaza.Application.Validators;

public class CreateReadingRequestValidator : AbstractValidator<CreateReadingRequest>
{
    public CreateReadingRequestValidator() : this(PragueClock.System)
    {
    }

    public CreateReadingRequestValidator(IClock clock)
    {
        RuleFor(x => x.MeterId)
            .NotEmpty().WithMessage("MeterId je povinné.")
            .Must(id => Guid.TryParse(id, out _)).WithMessage("MeterId musí být platné GUID.");

        RuleFor(x => x.ReadingDate)
            .NotEmpty().WithMessage("Datum odečtu je povinné.")
            .LessThan(PragueClock.AsUtcMidnight(clock.Today.AddDays(1))).WithMessage("Datum odečtu nesmí být v budoucnosti.");

        RuleFor(x => x.Value)
            .GreaterThanOrEqualTo(0).WithMessage("Hodnota musí být větší nebo rovna 0.");

        RuleFor(x => x.EstimateNote)
            .NotEmpty().When(x => x.IsEstimate).WithMessage("U odhadu uveďte, jak vznikl.")
            .MaximumLength(500).WithMessage("Popis odhadu může mít nejvýš 500 znaků.");
    }
}
