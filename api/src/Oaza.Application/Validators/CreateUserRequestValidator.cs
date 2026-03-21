using FluentValidation;
using Oaza.Application.DTOs;
using Oaza.Domain.Enums;

namespace Oaza.Application.Validators;

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(r => Enum.TryParse<UserRole>(r, ignoreCase: true, out _))
            .WithMessage("Role must be 'Admin', 'Member', or 'Accountant'.");

        RuleFor(x => x.HouseId)
            .Must(id => id is null || id.Length > 0)
            .WithMessage("HouseId must not be an empty string.");

        RuleFor(x => x.HouseId)
            .Must(id => Guid.TryParse(id, out _))
            .WithMessage("HouseId must be a valid GUID.")
            .When(x => x.HouseId is not null);

        RuleFor(x => x.AuthMethod)
            .NotEmpty().WithMessage("AuthMethod is required.")
            .Must(m => Enum.TryParse<AuthMethod>(m, ignoreCase: true, out _))
            .WithMessage("AuthMethod must be 'EntraId' or 'MagicLink'.");
    }
}
