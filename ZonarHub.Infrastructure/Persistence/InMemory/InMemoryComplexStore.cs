using System.Collections.Concurrent;
using ZonarHub.Domain.Complexes;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryComplexStore
{
    internal ConcurrentDictionary<ComplexId, Complex> Data { get; } = new();
}
