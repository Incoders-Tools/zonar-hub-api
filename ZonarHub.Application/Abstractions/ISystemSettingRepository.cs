using ZonarHub.Domain.SystemSettings;

namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Persistence contract for the <see cref="SystemSetting"/> aggregate.
/// </summary>
public interface ISystemSettingRepository
{
    Task<SystemSetting?> GetByIdAsync(SystemSettingId id, CancellationToken cancellationToken = default);

    Task<SystemSetting?> GetByKeyAsync(
        string key,
        SystemSettingScope scope,
        Guid? tenantId,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<SystemSetting> Items, int TotalCount)> ListAsync(
        SystemSettingQuery query,
        CancellationToken cancellationToken = default);

    Task AddAsync(SystemSetting setting, CancellationToken cancellationToken = default);

    void Update(SystemSetting setting);

    void Remove(SystemSetting setting);
}

/// <summary>
/// Repository-facing filter + pagination spec for listing system settings.
/// </summary>
public sealed record SystemSettingQuery(
    SystemSettingScope? Scope,
    Guid? TenantId,
    Guid? UserId,
    string? KeyContains,
    int Page,
    int PageSize);
