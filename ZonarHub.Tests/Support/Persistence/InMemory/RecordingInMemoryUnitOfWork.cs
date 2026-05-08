using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

/// <summary>
/// Test double that records how many times a handler called
/// <see cref="IUnitOfWork.SaveChangesAsync"/>. Used to lock in the
/// invariant that any handler mutating an enqueueing repository must
/// flush the unit of work — otherwise the request would silently
/// drop pending writes (the bug behind the complexes "phantom create").
/// </summary>
public sealed class RecordingInMemoryUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.FromResult(0);
    }
}
