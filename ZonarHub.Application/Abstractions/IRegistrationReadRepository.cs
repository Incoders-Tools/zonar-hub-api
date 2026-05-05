namespace ZonarHub.Application.Abstractions;

public interface IRegistrationReadRepository
{
    Task<int> CountByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
}