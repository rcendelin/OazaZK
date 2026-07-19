using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class CreateHouseRequestValidator : AbstractValidator<CreateHouseRequest>
{
    public CreateHouseRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Název je povinný.")
            .MaximumLength(200).WithMessage("Název nesmí přesáhnout 200 znaků.");

        RuleFor(x => x.Address)
            .NotEmpty().WithMessage("Adresa je povinná.")
            .MaximumLength(500).WithMessage("Adresa nesmí přesáhnout 500 znaků.");

        RuleFor(x => x.ContactPerson)
            .NotEmpty().WithMessage("Kontaktní osoba je povinná.")
            .MaximumLength(200).WithMessage("Kontaktní osoba nesmí přesáhnout 200 znaků.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email je povinný.")
            .EmailAddress().WithMessage("Zadejte platnou emailovou adresu.");
    }
}
