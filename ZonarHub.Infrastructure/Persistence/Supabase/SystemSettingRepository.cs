using Microsoft.Extensions.Options;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.SystemSettings;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class SystemSettingRepository : ISystemSettingRepository
{
    private readonly SupabaseOptions _options;

    public SystemSettingRepository(IOptions<SupabaseOptions> options)
    {
        _options = options.Value;
    }

    public Task<SystemSetting?> GetByIdAsync(SystemSettingId id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<SystemSetting?> GetByKeyAsync(
        string key,
        SystemSettingScope scope,
        Guid? tenantId,
        Guid? userId,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<(IReadOnlyList<SystemSetting> Items, int TotalCount)> ListAsync(
        SystemSettingQuery query,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task AddAsync(SystemSetting setting, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public void Update(SystemSetting setting)
        => throw new NotImplementedException();

    public void Remove(SystemSetting setting)
        => throw new NotImplementedException();
}
