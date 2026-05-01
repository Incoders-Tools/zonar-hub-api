using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryUserOrganizationAssignmentRepository : IUserOrganizationAssignmentRepository
{
    private readonly InMemoryUserOrganizationAssignmentStore _store;

    public InMemoryUserOrganizationAssignmentRepository(InMemoryUserOrganizationAssignmentStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<Guid>> GetOrganizationIdsByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (_store.Data.TryGetValue(userId, out var ids))
        {
            return Task.FromResult<IReadOnlyList<Guid>>(ids.ToList());
        }

        return Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());
    }

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetOrganizationIdsByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, IReadOnlyList<Guid>>();

        foreach (var userId in userIds)
        {
            if (_store.Data.TryGetValue(userId, out var ids))
            {
                result[userId] = ids.ToList();
            }
            else
            {
                result[userId] = Array.Empty<Guid>();
            }
        }

        return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(result);
    }

    public Task SetOrganizationIdsAsync(
        Guid userId,
        IReadOnlyList<Guid> organizationIds,
        CancellationToken cancellationToken = default)
    {
        _store.Data[userId] = organizationIds.Distinct().ToList();
        return Task.CompletedTask;
    }

    public Task RemoveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        _store.Data.TryRemove(userId, out _);
        return Task.CompletedTask;
    }
}
