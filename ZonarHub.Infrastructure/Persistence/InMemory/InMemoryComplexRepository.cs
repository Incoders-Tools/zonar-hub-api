using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Complexes;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryComplexRepository : IComplexRepository
{
    private readonly InMemoryComplexStore _store;

    public InMemoryComplexRepository(InMemoryComplexStore store)
    {
        _store = store;
    }

    public Task<Complex?> GetByIdAsync(ComplexId id, CancellationToken cancellationToken = default)
    {
        _store.Data.TryGetValue(id, out var complex);
        return Task.FromResult(complex);
    }

    public Task AddAsync(Complex complex, CancellationToken cancellationToken = default)
    {
        if (!_store.Data.TryAdd(complex.Id, complex))
            throw new InvalidOperationException($"Complex '{complex.Id}' already exists.");

        return Task.CompletedTask;
    }
}
