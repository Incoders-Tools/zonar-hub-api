using FluentValidation;

namespace ZonarHub.Application.Features.AdminUsers.Create;

internal sealed class CreateAdminUserValidator : AbstractValidator<CreateAdminUserCommand>
{
    public CreateAdminUserValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("admin.users.errors.email_required")
            .EmailAddress().WithMessage("admin.users.errors.email_invalid");

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("admin.users.errors.full_name_required")
            .MaximumLength(200).WithMessage("admin.users.errors.full_name_too_long");

        RuleFor(x => x.RoleId)
            .NotEmpty().WithMessage("admin.users.errors.role_id_required")
            .Must(roleId => AdminUserRoleMapper.TryFromRoleId(roleId, out _))
            .WithMessage("admin.users.errors.role_id_invalid");

        RuleForEach(x => x.TenantIds)
            .NotEqual(Guid.Empty)
            .WithMessage("admin.users.errors.organization_id_required")
            .When(x => x.TenantIds is not null);

        RuleFor(x => x.OrganizationId)
            .NotEqual(Guid.Empty)
            .WithMessage("admin.users.errors.organization_id_required")
            .When(x => x.OrganizationId.HasValue);
    }
}
