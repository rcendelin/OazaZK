using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class MagicLinkVerifyRequestValidator : AbstractValidator<MagicLinkVerifyRequest>
{
    public MagicLinkVerifyRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token is required.")
            .MaximumLength(100).WithMessage("Token must not exceed 100 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.");
    }
}
