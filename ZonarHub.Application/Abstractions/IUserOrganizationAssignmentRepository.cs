namespace ZonarHub.Application.Abstractions;

public interface IUserOrganizationAssignmentRepository
{
    Task<IReadOnlyList<Guid>> GetOrganizationIdsByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetOrganizationIdsByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    Task SetOrganizationIdsAsync(
        Guid userId,
        IReadOnlyList<Guid> organizationIds,
        CancellationToken cancellationToken = default);

    Task RemoveByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
