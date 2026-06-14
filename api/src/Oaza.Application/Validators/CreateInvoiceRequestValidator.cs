using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class CreateInvoiceRequestValidator : AbstractValidator<CreateInvoiceRequest>
{
    public CreateInvoiceRequestValidator()
    {
        RuleFor(x => x.InvoiceNumber)
            .NotEmpty().WithMessage("Číslo faktury je povinné.")
            .MaximumLength(50).WithMessage("Číslo faktury smí mít max 50 znaků.");

        RuleFor(x => x.VatRatePercent)
            .InclusiveBetween(0, 100).WithMessage("Sazba DPH musí být v rozsahu 0–100 %.");

        RuleFor(x => x.LineItems)
            .NotEmpty().WithMessage("Faktura musí mít alespoň jeden řádek (dílčí odečet).");

        RuleForEach(x => x.LineItems).ChildRules(line =>
        {
            line.RuleFor(l => l.DateFrom)
                .LessThanOrEqualTo(l => l.DateTo).WithMessage("Období od musí být před nebo rovno období do.");
            line.RuleFor(l => l.ConsumptionM3)
                .GreaterThanOrEqualTo(0).WithMessage("Spotřeba řádku musí být ≥ 0.");
            line.RuleFor(l => l.AmountExclVat)
                .GreaterThanOrEqualTo(0).WithMessage("Cena řádku musí být ≥ 0.");
        });
    }
}
