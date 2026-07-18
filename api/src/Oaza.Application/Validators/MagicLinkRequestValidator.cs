using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class MagicLinkRequestValidator : AbstractValidator<MagicLinkRequest>
{
    public MagicLinkRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email je povinný.")
            .EmailAddress().WithMessage("Zadejte platnou emailovou adresu.");
    }
}
