using System.Collections.Concurrent;
using ZonarHub.Domain.Sports;
using ZonarHub.Domain.Tenants;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryTenantSportStore
{
    // Key: TenantId -> set of enabled SportIds
    private readonly ConcurrentDictionary<TenantId, HashSet<SportId>> _data = new();

    internal ConcurrentDictionary<TenantId, HashSet<SportId>> Data => _data;
}
