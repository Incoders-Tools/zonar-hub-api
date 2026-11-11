using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.EligibilityProfiles;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.Players;

/// <summary>Creates an active player of the organization without a user link (S3).</summary>
public sealed record CreatePlayerCommand(Guid OrganizationId, PlayerBody Body) : IRequest<Result<PlayerResponse>>;

/// <remarks>
/// Order: tenant rule (<see cref="AdminTournamentErrors.Forbidden"/>), catalog references
/// (<see cref="PlayerErrors.UnknownGender"/>, <see cref="PlayerErrors.UnknownCategory"/>), the <see cref="Player"/> rules,
/// then the store's unique email and document (<see cref="PlayerErrors.EmailTaken"/>, <see cref="PlayerErrors.DocumentTaken"/>).
/// Nothing is written on a failure.
/// </remarks>
public sealed class CreatePlayerHandler : IRequestHandler<CreatePlayerCommand, Result<PlayerResponse>>
{
    private readonly TournamentOrganizationAccess _access;
    private readonly IPlayerRepository _players;
    private readonly CatalogReferences _catalogs;
    private readonly IClock _clock;

    public CreatePlayerHandler(
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

    public async Task<Result<PlayerResponse>> Handle(CreatePlayerCommand request, CancellationToken cancellationToken)
    {
        if (!await _access.CanAccessAsync(request.OrganizationId, cancellationToken))
            return Result.Failure<PlayerResponse>(AdminTournamentErrors.Forbidden);

        var references = await CheckReferencesAsync(_catalogs, request.Body, cancellationToken);
        if (references.IsFailure)
            return Result.Failure<PlayerResponse>(references.Error);

        var created = Player.Create(
            PlayerId.New(), new OrganizationId(request.OrganizationId), null, PlayerMapper.ToDetails(request.Body), _clock.UtcNow);
        if (created.IsFailure)
            return Result.Failure<PlayerResponse>(created.Error);

        var written = await PersistenceWrites.TryAsync(() => _players.AddAsync(created.Value, cancellationToken));
        return written.IsFailure
            ? Result.Failure<PlayerResponse>(written.Error)
            : Result.Success(PlayerMapper.ToResponse(created.Value));
    }

    /// <summary>The body's gender and category exist in the catalogs.</summary>
    internal static async Task<Result> CheckReferencesAsync(CatalogReferences catalogs, PlayerBody body, CancellationToken cancellationToken)
    {
        if (body.GenderId is null && body.CategoryId is null)
            return Result.Success();

        var catalog = await catalogs.LoadAsync(cancellationToken);
        if (!catalog.HasGender(body.GenderId))
            return Result.Failure(PlayerErrors.UnknownGender);

        return catalog.HasCategory(body.CategoryId) ? Result.Success() : Result.Failure(PlayerErrors.UnknownCategory);
    }
}
