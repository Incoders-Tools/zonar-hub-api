using FluentValidation;

namespace ZonarHub.Application.Features.AdminRoles.Update;

internal sealed class UpdateAdminRoleValidator : AbstractValidator<UpdateAdminRoleCommand>
{
    public UpdateAdminRoleValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("admin.roles.errors.id_required");

        RuleFor(x => x.Name)
            .MaximumLength(50).WithMessage("admin.roles.errors.name_too_long")
            .Must(value => string.IsNullOrWhiteSpace(value) || AdminRoleName.IsValid(AdminRoleName.Normalize(value)))
            .WithMessage("admin.roles.errors.name_invalid");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("admin.roles.errors.description_too_long");
    }
}
