using System.Collections.Concurrent;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryRegistrationStore
{
    private readonly ConcurrentDictionary<Guid, int> _counts = new();

    public void Seed(Guid organizationId, int count)
    {
        if (count <= 0)
        {
            return;
        }

        _counts.AddOrUpdate(organizationId, count, (_, current) => current + count);
    }

    public int CountByOrganization(Guid organizationId)
    {
        return _counts.TryGetValue(organizationId, out var count) ? count : 0;
    }
}