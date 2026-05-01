using FluentValidation;

namespace ZonarHub.Application.Features.Organizations.Update;

internal sealed class UpdateOrganizationValidator : AbstractValidator<UpdateOrganizationCommand>
{
    public UpdateOrganizationValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty).WithMessage("organizations.errors.id_required");
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("organizations.errors.display_name_required")
            .MaximumLength(200).WithMessage("organizations.errors.display_name_too_long");
        RuleFor(x => x.LegalName)
            .MaximumLength(300).WithMessage("organizations.errors.legal_name_too_long")
            .When(x => x.LegalName is not null);
        RuleFor(x => x.Type).IsInEnum().WithMessage("organizations.errors.type_invalid");
    }
}
