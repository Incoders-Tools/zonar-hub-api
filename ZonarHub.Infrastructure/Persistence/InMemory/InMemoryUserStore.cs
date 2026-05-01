using System.Collections.Concurrent;
using ZonarHub.Domain.Users;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryUserStore
{
    internal ConcurrentDictionary<UserId, User> Data { get; } = new();
}
