using System.Collections.Concurrent;
using ZonarHub.Domain.SocialNetworks;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemorySocialNetworkStore
{
    private readonly ConcurrentDictionary<SocialNetworkId, SocialNetwork> _data = new();

    internal ConcurrentDictionary<SocialNetworkId, SocialNetwork> Data => _data;
}
