using System.Collections.Concurrent;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryUserOrganizationPermissionStore
{
    internal ConcurrentDictionary<Guid, List<UserOrganizationPermission>> Data { get; } = new();
}
