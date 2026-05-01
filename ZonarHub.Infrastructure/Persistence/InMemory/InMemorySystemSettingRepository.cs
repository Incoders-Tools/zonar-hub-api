using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.SystemSettings;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemorySystemSettingRepository : ISystemSettingRepository
{
    private readonly InMemorySystemSettingStore _store;

    public InMemorySystemSettingRepository(InMemorySystemSettingStore store)
    {
        _store = store;
    }

    public Task<SystemSetting?> GetByIdAsync(SystemSettingId id, CancellationToken cancellationToken = default)
    {
        _store.Settings.TryGetValue(id, out var setting);
        return Task.FromResult(setting);
    }

    public Task<SystemSetting?> GetByKeyAsync(
        string key,
        SystemSettingScope scope,
        Guid? tenantId,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var match = _store.Settings.Values.FirstOrDefault(s =>
            string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase) &&
            s.Scope == scope &&
            s.TenantId == tenantId &&
            s.UserId == userId);
        return Task.FromResult(match);
    }

    public Task<(IReadOnlyList<SystemSetting> Items, int TotalCount)> ListAsync(
        SystemSettingQuery query,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<SystemSetting> source = _store.Settings.Values;

        if (query.Scope is { } scope)
        {
            source = source.Where(s => s.Scope == scope);
        }

        if (query.TenantId is { } tenantId)
        {
            source = source.Where(s => s.TenantId == tenantId);
        }

        if (query.UserId is { } userId)
        {
            source = source.Where(s => s.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(query.KeyContains))
        {
            var needle = query.KeyContains.Trim();
            source = source.Where(s => s.Key.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = source
            .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.CreatedAtUtc)
            .ToList();
        var total = filtered.Count;
        IReadOnlyList<SystemSetting> page = filtered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return Task.FromResult((page, total));
    }

    public Task AddAsync(SystemSetting setting, CancellationToken cancellationToken = default)
    {
        if (!_store.Settings.TryAdd(setting.Id, setting))
        {
            throw new InvalidOperationException($"SystemSetting '{setting.Id}' already exists in the in-memory store.");
        }

        return Task.CompletedTask;
    }

    public void Update(SystemSetting setting)
    {
        _store.Settings[setting.Id] = setting;
    }

    public void Remove(SystemSetting setting)
    {
        _store.Settings.TryRemove(setting.Id, out _);
    }
}
