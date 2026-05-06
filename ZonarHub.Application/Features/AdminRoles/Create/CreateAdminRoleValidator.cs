using FluentValidation;

namespace ZonarHub.Application.Features.AdminRoles.Create;

internal sealed class CreateAdminRoleValidator : AbstractValidator<CreateAdminRoleCommand>
{
    public CreateAdminRoleValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("admin.roles.errors.name_required")
            .MaximumLength(50).WithMessage("admin.roles.errors.name_too_long")
            .Must(value => AdminRoleName.IsValid(AdminRoleName.Normalize(value)))
            .WithMessage("admin.roles.errors.name_invalid");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("admin.roles.errors.description_required")
            .MaximumLength(500).WithMessage("admin.roles.errors.description_too_long");
    }
}
