using ZonarHub.Application.Abstractions;

namespace ZonarHub.Application.Features.AdminRoles;

public sealed record AdminRoleResponse(
    string Id,
    string Name,
    string Description,
    bool IsActive,
    bool IsSystem,
    DateTime CreatedAt,
    DateTime UpdatedAt);

internal static class AdminRoleMappings
{
    public static AdminRoleResponse ToResponse(this RoleDefinition role) => new(
        role.Id,
        role.Name,
        role.Description,
        role.IsActive,
        role.IsSystem,
        role.CreatedAtUtc,
        role.UpdatedAtUtc);
}
