using System.Collections.Concurrent;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryUserOrganizationAssignmentStore
{
    // Key: user id -> ordered organization ids.
    internal ConcurrentDictionary<Guid, List<Guid>> Data { get; } = new();
}
