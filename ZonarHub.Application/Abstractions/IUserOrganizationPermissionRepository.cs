namespace ZonarHub.Application.Abstractions;

public interface IUserOrganizationPermissionRepository
{
    Task<IReadOnlyList<UserOrganizationPermission>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetToolKeysAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task SetPermissionsAsync(
        Guid userId,
        IReadOnlyList<UserOrganizationPermission> permissions,
        CancellationToken cancellationToken = default);

    Task RemoveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record UserOrganizationPermission(
    Guid OrganizationId,
    string ToolKey);
