using FluentValidation;
using Oaza.Application.DTOs;

namespace Oaza.Application.Validators;

public class UploadDocumentRequestValidator : AbstractValidator<UploadDocumentRequest>
{
    private static readonly string[] AllowedCategories = { "stanovy", "zapisy", "smlouvy", "ostatni" };

    public UploadDocumentRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Document name is required.")
            .MaximumLength(200).WithMessage("Document name must not exceed 200 characters.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required.")
            .Must(c => AllowedCategories.Contains(c, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Category must be one of: {string.Join(", ", AllowedCategories)}.");
    }
}
