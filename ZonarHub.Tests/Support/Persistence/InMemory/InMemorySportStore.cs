using System.Collections.Concurrent;
using ZonarHub.Domain.Sports;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemorySportStore
{
    private readonly ConcurrentDictionary<SportId, Sport> _data = new();

    internal ConcurrentDictionary<SportId, Sport> Data => _data;
}
