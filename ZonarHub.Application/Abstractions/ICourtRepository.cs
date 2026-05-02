using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Courts;

namespace ZonarHub.Application.Abstractions;

public interface ICourtRepository
{
    Task<IReadOnlyList<Court>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Court?> GetByIdAsync(CourtId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Court>> ListByComplexIdAsync(ComplexId complexId, CancellationToken cancellationToken = default);

    Task AddAsync(Court court, CancellationToken cancellationToken = default);

    void Update(Court court);

    void Remove(Court court);
}
