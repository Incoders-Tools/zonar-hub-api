using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminUsers;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminTournaments.GetAll;

public sealed class GetTournamentsHandler
    : IRequestHandler<GetTournamentsQuery, Result<IReadOnlyList<TournamentResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IOrganizationRepository _organizations;
    private readonly ITournamentAdminRepository _tournaments;

    public GetTournamentsHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments)
    {
        _currentUser = currentUser;
        _users = users;
        _organizations = organizations;
        _tournaments = tournaments;
    }

    public async Task<Result<IReadOnlyList<TournamentResponse>>> Handle(
        GetTournamentsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<IReadOnlyList<TournamentResponse>>(AdminTournamentErrors.OrganizationRequired);
        }

        if (!await CanReadOrganizationAsync(request.OrganizationId, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<TournamentResponse>>(AdminTournamentErrors.Forbidden);
        }

        var rows = await _tournaments.ListByOrganizationAsync(request.OrganizationId, cancellationToken);
        IReadOnlyList<TournamentResponse> mapped = rows.Select(TournamentMapper.ToResponse).ToList();
        return Result.Success(mapped);
    }

    /// <summary>
    /// Fails closed: system administrators may read any existing organization; any other caller only an
    /// existing organization of their own tenant. The X-Organization-Id header is never trusted here.
    /// </summary>
    private async Task<bool> CanReadOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
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
