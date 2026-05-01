using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Courts;

namespace ZonarHub.Application.Abstractions;

public interface ICourtRepository
{
    Task<IReadOnlyList<Court>> ListByComplexIdAsync(ComplexId complexId, CancellationToken cancellationToken = default);

    Task AddAsync(Court court, CancellationToken cancellationToken = default);
}
