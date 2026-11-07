using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminTournaments.GetById;

public sealed class GetTournamentByIdHandler
    : IRequestHandler<GetTournamentByIdQuery, Result<TournamentResponse>>
{
    private readonly TournamentOrganizationAccess _access;
    private readonly ITournamentAdminRepository _tournaments;

    public GetTournamentByIdHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments)
    {
        _access = new TournamentOrganizationAccess(currentUser, users, organizations);
        _tournaments = tournaments;
    }

    public async Task<Result<TournamentResponse>> Handle(
        GetTournamentByIdQuery request,
        CancellationToken cancellationToken)
    {
        var current = await _tournaments.GetByIdAsync(request.Id, cancellationToken);
        if (current is null)
        {
            return Result.Failure<TournamentResponse>(AdminTournamentErrors.NotFound);
        }

        if (!await _access.CanAccessAsync(current.OrganizationId, cancellationToken))
        {
            return Result.Failure<TournamentResponse>(AdminTournamentErrors.Forbidden);
        }

        return Result.Success(TournamentMapper.ToResponse(current));
    }
}
