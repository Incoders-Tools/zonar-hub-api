using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryComplexRepository : IComplexRepository
{
    private readonly InMemoryComplexStore _store;

    public InMemoryComplexRepository(InMemoryComplexStore store)
    {
        _store = store;
    }

    public Task<SavedComplexWithCourtsData> SaveWithCourtsAsync(SaveComplexWithCourtsData data, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Aggregate transactional writes require a transactional persistence adapter.");

    public Task<Complex?> GetByIdAsync(ComplexId id, CancellationToken cancellationToken = default)
    {
        _store.Data.TryGetValue(id, out var complex);
        return Task.FromResult(complex);
    }

    public Task<IReadOnlyList<Complex>> ListByOrganizationAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Complex> result = _store.Data.Values
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(result);
    }

    public Task AddAsync(Complex complex, CancellationToken cancellationToken = default)
    {
        if (!_store.Data.TryAdd(complex.Id, complex))
            throw new InvalidOperationException($"Complex '{complex.Id}' already exists.");

        return Task.CompletedTask;
    }

    public Task UpdateAsync(Complex complex, CancellationToken cancellationToken = default)
    {
        _store.Data[complex.Id] = complex;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(ComplexId id, CancellationToken cancellationToken = default)
    {
        var removed = _store.Data.TryRemove(id, out _);
        return Task.FromResult(removed);
    }
}
