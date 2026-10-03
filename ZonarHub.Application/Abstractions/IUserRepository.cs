using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> ExistsByPhoneAsync(string phone, CancellationToken cancellationToken = default);

    Task<User?> GetByRefreshTokenAsync(string token, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<User> Items, int TotalCount)> ListAsync(
        UserQuery query,
        CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);

    void Update(User user);

    Task RemoveAsync(UserId id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Filters and pages a user list.
/// </summary>
/// <param name="Membership">Optional organization membership restriction.</param>
/// <param name="ExcludeRole">
/// When set, users with this role are removed before paging and counting.
/// Not yet supported together with <paramref name="Membership"/>; repositories reject that combination.
/// </param>
public sealed record UserQuery(
    Guid? TenantId,
    string? Search,
    UserRole? Role,
    bool? IsActive,
    int Page,
    int PageSize,
    UserOrganizationMembership? Membership = null,
    UserRole? ExcludeRole = null);

/// <summary>
/// Restricts a user list to members of one organization, applied before paging and counting.
/// A user is a member when the organization is their primary organization or one of their assignments.
/// </summary>
/// <param name="OrganizationId">Organization whose members are listed.</param>
/// <param name="IncludeUnassignedOfTenantId">
/// When set, also includes users of this tenant that have neither a primary organization nor assignments.
/// </param>
public sealed record UserOrganizationMembership(
    Guid OrganizationId,
    Guid? IncludeUnassignedOfTenantId);
