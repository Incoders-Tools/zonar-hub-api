namespace ZonarHub.Application.Abstractions;

public interface IRoleRepository
{
    Task<IReadOnlyList<RoleDefinition>> ListAsync(CancellationToken cancellationToken = default);

    Task<RoleDefinition?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<RoleDefinition?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task AddAsync(RoleDefinition role, CancellationToken cancellationToken = default);

    void Update(RoleDefinition role);

    Task RemoveAsync(string id, CancellationToken cancellationToken = default);
}

public sealed record RoleDefinition(
    string Id,
    string Name,
    string Description,
    bool IsActive,
    bool IsSystem,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
