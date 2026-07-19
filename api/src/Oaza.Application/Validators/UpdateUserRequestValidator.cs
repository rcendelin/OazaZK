using FluentValidation;
using Oaza.Application.DTOs;
using Oaza.Domain.Enums;

namespace Oaza.Application.Validators;

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Jméno je povinné.")
            .MaximumLength(200).WithMessage("Jméno nesmí přesáhnout 200 znaků.");

        RuleFor(x => x.Role)
            .Must(r => r is null || Enum.TryParse<UserRole>(r, ignoreCase: true, out _))
            .WithMessage("Role musí být 'Admin', 'Member' nebo 'Accountant'.");

        RuleFor(x => x.HouseId)
            .Must(id => id is null || id.Length > 0)
            .WithMessage("HouseId nesmí být prázdný řetězec.");

        RuleFor(x => x.HouseId)
            .Must(id => Guid.TryParse(id, out _))
            .WithMessage("HouseId musí být platné GUID.")
            .When(x => x.HouseId is not null);
    }
}
