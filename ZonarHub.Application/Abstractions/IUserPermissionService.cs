using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Abstractions;

public interface IUserPermissionService
{
    Task<IReadOnlyList<string>> GetEffectiveToolKeysAsync(
        User user,
        Guid? organizationId,
        CancellationToken cancellationToken = default);

    Task<bool> HasToolAsync(
        User user,
        Guid? organizationId,
        string toolKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetDefaultToolKeysAsync(
        UserRole role,
        CancellationToken cancellationToken = default);
}
