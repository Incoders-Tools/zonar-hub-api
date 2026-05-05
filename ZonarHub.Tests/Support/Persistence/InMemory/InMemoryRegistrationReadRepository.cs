using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryRegistrationReadRepository : IRegistrationReadRepository
{
    private readonly InMemoryRegistrationStore _store;

    public InMemoryRegistrationReadRepository(InMemoryRegistrationStore store)
    {
        _store = store;
    }

    public Task<int> CountByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_store.CountByOrganization(organizationId));
    }
}