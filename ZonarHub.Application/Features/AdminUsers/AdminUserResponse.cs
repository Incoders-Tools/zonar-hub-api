using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminUsers;

public sealed record AdminUserResponse(
    Guid Id,
    string Email,
    string FullName,
    string? Phone,
    string Role,
    string RoleId,
    string RoleName,
    Guid? OrganizationId,
    string? OrganizationName,
    IReadOnlyList<Guid> TenantIds,
    IReadOnlyList<string> TenantNames,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static AdminUserResponse FromDomain(
        User user,
        IReadOnlyList<Guid> tenantIds,
        IReadOnlyDictionary<Guid, string> organizationNames)
    {
        var roleName = AdminUserRoleMapper.ToRoleName(user.Role);

        var tenantNames = tenantIds
            .Select(id => organizationNames.TryGetValue(id, out var name) ? name : id.ToString())
            .ToList();

        var organizationName = user.OrganizationId is { } organizationId &&
            organizationNames.TryGetValue(organizationId, out var orgName)
            ? orgName
            : null;

        return new AdminUserResponse(
            user.Id.Value,
            user.Email,
            user.FullName,
            user.Phone,
            roleName,
            AdminUserRoleMapper.ToRoleId(user.Role),
            roleName,
            user.OrganizationId,
            organizationName,
            tenantIds,
            tenantNames,
            user.IsActive,
            user.CreatedAtUtc,
            user.UpdatedAtUtc);
    }
}
