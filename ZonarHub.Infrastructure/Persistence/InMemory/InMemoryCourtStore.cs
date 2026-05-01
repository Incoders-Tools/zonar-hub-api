using System.Collections.Concurrent;
using ZonarHub.Domain.Courts;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryCourtStore
{
    internal ConcurrentDictionary<CourtId, Court> Data { get; } = new();
}
