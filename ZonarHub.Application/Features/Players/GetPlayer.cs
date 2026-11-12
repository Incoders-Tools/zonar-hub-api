using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.Players;

/// <summary>The organization's player; <see cref="PlayerErrors.NotFound"/> also for a player of another organization.</summary>
public sealed record GetPlayerQuery(Guid OrganizationId, Guid PlayerId) : IRequest<Result<PlayerResponse>>;

public sealed class GetPlayerHandler : IRequestHandler<GetPlayerQuery, Result<PlayerResponse>>
{
    private readonly TournamentOrganizationAccess _access;
    private readonly IPlayerRepository _players;

    public GetPlayerHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        IPlayerRepository players)
    {
        _access = new TournamentOrganizationAccess(currentUser, users, organizations);
        _players = players;
    }

    public async Task<Result<PlayerResponse>> Handle(GetPlayerQuery request, CancellationToken cancellationToken)
    {
        if (!await _access.CanAccessAsync(request.OrganizationId, cancellationToken))
            return Result.Failure<PlayerResponse>(AdminTournamentErrors.Forbidden);

        var player = await _players.GetByIdAsync(new OrganizationId(request.OrganizationId), new PlayerId(request.PlayerId), cancellationToken);
        return player is null
            ? Result.Failure<PlayerResponse>(PlayerErrors.NotFound)
            : Result.Success(PlayerMapper.ToResponse(player));
    }
}
