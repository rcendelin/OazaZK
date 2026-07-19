using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class MagicLinkVerifyRequestValidator : AbstractValidator<MagicLinkVerifyRequest>
{
    public MagicLinkVerifyRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token je povinný.")
            .MaximumLength(100).WithMessage("Token nesmí přesáhnout 100 znaků.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email je povinný.")
            .EmailAddress().WithMessage("Zadejte platnou emailovou adresu.");
    }
}
