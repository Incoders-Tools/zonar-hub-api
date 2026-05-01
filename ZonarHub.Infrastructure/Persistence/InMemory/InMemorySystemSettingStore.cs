using System.Collections.Concurrent;
using ZonarHub.Domain.SystemSettings;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemorySystemSettingStore
{
    private readonly ConcurrentDictionary<SystemSettingId, SystemSetting> _settings = new();

    internal ConcurrentDictionary<SystemSettingId, SystemSetting> Settings => _settings;
}
