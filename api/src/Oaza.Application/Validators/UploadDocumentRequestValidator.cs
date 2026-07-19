using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class UploadDocumentRequestValidator : AbstractValidator<UploadDocumentRequest>
{
    private static readonly string[] AllowedCategories = { "stanovy", "zapisy", "smlouvy", "ostatni" };

    public UploadDocumentRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Název dokumentu je povinný.")
            .MaximumLength(200).WithMessage("Název dokumentu nesmí přesáhnout 200 znaků.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Kategorie je povinná.")
            .Must(c => AllowedCategories.Contains(c, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Kategorie musí být jedna z: {string.Join(", ", AllowedCategories)}.");
    }
}
