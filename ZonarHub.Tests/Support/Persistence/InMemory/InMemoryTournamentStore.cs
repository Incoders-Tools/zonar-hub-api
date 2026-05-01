using System.Collections.Concurrent;
using ZonarHub.Domain.Tournaments;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryTournamentStore
{
    internal ConcurrentDictionary<TournamentId, Tournament> Data { get; } = new();
}
