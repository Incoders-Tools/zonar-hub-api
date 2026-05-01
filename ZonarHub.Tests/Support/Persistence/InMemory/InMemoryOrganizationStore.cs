using System.Collections.Concurrent;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryOrganizationStore
{
    private readonly ConcurrentDictionary<OrganizationId, Organization> _data = new();

    internal ConcurrentDictionary<OrganizationId, Organization> Data => _data;
}
