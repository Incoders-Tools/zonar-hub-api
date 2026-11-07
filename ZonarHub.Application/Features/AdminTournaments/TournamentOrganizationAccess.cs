using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminUsers;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminTournaments;

/// <summary>
/// The single tenant rule shared by every admin tournament use case.
/// </summary>
internal sealed class TournamentOrganizationAccess
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IOrganizationRepository _organizations;

    public TournamentOrganizationAccess(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations)
    {
        _currentUser = currentUser;
        _users = users;
        _organizations = organizations;
    }

    /// <summary>
    /// Fails closed: system administrators may access any existing organization; any other caller only an
    /// existing organization of their own tenant. The X-Organization-Id header is never trusted here.
    /// </summary>
    public async Task<bool> CanAccessAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            return false;
        }

        var caller = await _users.GetByIdAsync(new UserId(_currentUser.UserId.Value), cancellationToken);
        if (caller is null)
        {
            return false;
        }

        var organization = await _organizations.GetByIdAsync(new OrganizationId(organizationId), cancellationToken);
        if (organization is null)
        {
            return false;
        }

        return AdminUserAuthorization.IsSystemAdmin(caller) ||
            (caller.TenantId is { } callerTenantId && organization.TenantId == callerTenantId);
    }
}
