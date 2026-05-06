using FluentValidation;

namespace ZonarHub.Application.Features.AdminUsers.Update;

internal sealed class UpdateAdminUserValidator : AbstractValidator<UpdateAdminUserCommand>
{
    public UpdateAdminUserValidator()
    {
        RuleFor(x => x.UserId)
            .NotEqual(Guid.Empty)
            .WithMessage("admin.users.errors.user_id_required");

        RuleFor(x => x.RoleId)
            .Must(roleId => string.IsNullOrWhiteSpace(roleId) || AdminUserRoleMapper.TryFromRoleId(roleId, out _))
            .WithMessage("admin.users.errors.role_id_invalid");

        RuleForEach(x => x.TenantIds)
            .NotEqual(Guid.Empty)
            .WithMessage("admin.users.errors.organization_id_required")
            .When(x => x.TenantIds is not null);

        RuleFor(x => x.OrganizationId)
            .NotEqual(Guid.Empty)
            .WithMessage("admin.users.errors.organization_id_required")
            .When(x => x.OrganizationId.HasValue);

        RuleForEach(x => x.PermissionsByOrganization)
            .ChildRules(permission =>
            {
                permission.RuleFor(x => x.OrganizationId)
                    .NotEqual(Guid.Empty)
                    .WithMessage("admin.users.errors.organization_id_required");

                permission.RuleForEach(x => x.ToolKeys)
                    .NotEmpty()
                    .WithMessage("admin.users.errors.tool_key_required");
            })
            .When(x => x.PermissionsByOrganization is not null);
    }
}
