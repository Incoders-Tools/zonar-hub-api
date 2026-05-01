using ZonarHub.Domain.Complexes;

namespace ZonarHub.Application.Abstractions;

public interface IComplexRepository
{
    Task<Complex?> GetByIdAsync(ComplexId id, CancellationToken cancellationToken = default);

    Task AddAsync(Complex complex, CancellationToken cancellationToken = default);
}
