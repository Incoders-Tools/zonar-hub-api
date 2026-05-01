using System.Collections.Concurrent;
using ZonarHub.Domain.Tenants;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryTenantStore
{
    internal ConcurrentDictionary<TenantId, Tenant> Data { get; } = new();
}
