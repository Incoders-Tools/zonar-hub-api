using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Application.Abstractions;

public interface IComplexRepository
{
    Task<Complex?> GetByIdAsync(ComplexId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Complex>> ListByOrganizationAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default);

    Task AddAsync(Complex complex, CancellationToken cancellationToken = default);
    Task UpdateAsync(Complex complex, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(ComplexId id, CancellationToken cancellationToken = default);
}
