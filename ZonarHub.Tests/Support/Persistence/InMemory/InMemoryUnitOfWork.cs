using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

/// <summary>
/// No-op unit of work for the in-memory persistence adapter.
/// </summary>
/// <remarks>
/// Mutations happen directly on the shared store, so there is nothing to commit.
/// The abstraction exists to keep application-layer code identical when a real
/// persistence provider is swapped in.
/// </remarks>
public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}
