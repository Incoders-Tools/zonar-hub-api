using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.EligibilityProfiles;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.Players;

/// <summary>
/// Replaces the details and activity of the organization's player; the user link is kept. Players are never deleted:
/// their registrations keep referencing them, so <c>isActive: false</c> retires a player.
/// </summary>
public sealed record UpdatePlayerCommand(Guid OrganizationId, Guid PlayerId, PlayerBody Body) : IRequest<Result<PlayerResponse>>;

/// <remarks>
/// Same order and errors as <see cref="CreatePlayerHandler"/>, after <see cref="PlayerErrors.NotFound"/> for an unknown
/// id or a player of another organization. Nothing is written on a failure.
/// </remarks>
public sealed class UpdatePlayerHandler : IRequestHandler<UpdatePlayerCommand, Result<PlayerResponse>>
{
    private readonly TournamentOrganizationAccess _access;
    private readonly IPlayerRepository _players;
    private readonly CatalogReferences _catalogs;
    private readonly IClock _clock;

    public UpdatePlayerHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        IPlayerRepository players,
        IGenderRepository genders,
        ICategoryRepository categories,
        IClock clock)
    {
        _access = new TournamentOrganizationAccess(currentUser, users, organizations);
        _players = players;
        _catalogs = new CatalogReferences(genders, categories);
        _clock = clock;
    }

    public async Task<Result<PlayerResponse>> Handle(UpdatePlayerCommand request, CancellationToken cancellationToken)
    {
        if (!await _access.CanAccessAsync(request.OrganizationId, cancellationToken))
            return Result.Failure<PlayerResponse>(AdminTournamentErrors.Forbidden);

        var player = await _players.GetByIdAsync(new OrganizationId(request.OrganizationId), new PlayerId(request.PlayerId), cancellationToken);
        if (player is null)
            return Result.Failure<PlayerResponse>(PlayerErrors.NotFound);

        var references = await CreatePlayerHandler.CheckReferencesAsync(_catalogs, request.Body, cancellationToken);
        if (references.IsFailure)
            return Result.Failure<PlayerResponse>(references.Error);

        var updated = player.Update(PlayerMapper.ToDetails(request.Body), request.Body.IsActive, player.UserId, _clock.UtcNow);
        if (updated.IsFailure)
            return Result.Failure<PlayerResponse>(updated.Error);

        var written = await PersistenceWrites.TryAsync(() => _players.UpdateAsync(player, cancellationToken));
        return written.IsFailure
            ? Result.Failure<PlayerResponse>(written.Error)
            : Result.Success(PlayerMapper.ToResponse(player));
    }
}
