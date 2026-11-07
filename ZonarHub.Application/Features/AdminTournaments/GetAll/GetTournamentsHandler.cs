using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminTournaments.GetAll;

public sealed class GetTournamentsHandler
    : IRequestHandler<GetTournamentsQuery, Result<IReadOnlyList<TournamentResponse>>>
{
    private readonly TournamentOrganizationAccess _access;
    private readonly ITournamentAdminRepository _tournaments;

    public GetTournamentsHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments)
    {
        _access = new TournamentOrganizationAccess(currentUser, users, organizations);
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

        if (!await _access.CanAccessAsync(request.OrganizationId, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<TournamentResponse>>(AdminTournamentErrors.Forbidden);
        }

        var rows = await _tournaments.ListByOrganizationAsync(request.OrganizationId, cancellationToken);
        IReadOnlyList<TournamentResponse> mapped = rows.Select(TournamentMapper.ToResponse).ToList();
        return Result.Success(mapped);
    }
}
