using System.Collections.Concurrent;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Sports;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryOrganizationSportStore
{
    // Key: OrganizationId → set of enabled SportIds
    private readonly ConcurrentDictionary<OrganizationId, HashSet<SportId>> _data = new();

    internal ConcurrentDictionary<OrganizationId, HashSet<SportId>> Data => _data;
}
