namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Persists changes tracked within the current business transaction.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
