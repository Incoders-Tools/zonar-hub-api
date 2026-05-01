using FluentValidation;

namespace ZonarHub.Application.Features.Organizations.Create;

internal sealed class CreateOrganizationValidator : AbstractValidator<CreateOrganizationCommand>
{
    public const int DisplayNameMaxLength = 200;
    public const int LegalNameMaxLength = 300;

    public CreateOrganizationValidator()
    {
        RuleFor(x => x.TenantId).NotEqual(Guid.Empty).WithMessage("organizations.errors.tenant_id_required");
        RuleFor(x => x.CreatedByUserId).NotEqual(Guid.Empty).WithMessage("organizations.errors.user_id_required");
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("organizations.errors.display_name_required")
            .MaximumLength(DisplayNameMaxLength).WithMessage("organizations.errors.display_name_too_long");
        RuleFor(x => x.LegalName)
            .MaximumLength(LegalNameMaxLength).WithMessage("organizations.errors.legal_name_too_long")
            .When(x => x.LegalName is not null);
        RuleFor(x => x.Type).IsInEnum().WithMessage("organizations.errors.type_invalid");
    }
}
