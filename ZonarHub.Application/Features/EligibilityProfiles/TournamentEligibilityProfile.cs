using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Registrations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.EligibilityProfiles;

/// <summary>
/// Body and response of <c>GET</c> and <c>PUT /api/admin/tournaments/{tournamentId}/eligibility-profile</c> (R2b).
/// </summary>
/// <param name="EligibilityProfileId">
/// A profile of the tournament's organization, or null for none. An inactive profile may be assigned; while inactive it
/// is ignored and the tournament's own rules apply (L8).
/// </param>
public sealed record TournamentEligibilityProfileBody(Guid? EligibilityProfileId);

/// <summary>The eligibility profile the tournament uses, or null.</summary>
public sealed record GetTournamentEligibilityProfileQuery(Guid TournamentId) : IRequest<Result<TournamentEligibilityProfileBody>>;

/// <summary>Assigns a profile of the tournament's organization to the tournament; null clears it.</summary>
public sealed record SetTournamentEligibilityProfileCommand(Guid TournamentId, Guid? EligibilityProfileId)
    : IRequest<Result<TournamentEligibilityProfileBody>>;

/// <remarks>
/// Tournament tenant rule of <see cref="TournamentDrawScope.LoadTournamentAsync"/> first (404 <c>tournaments.not_found</c>,
/// 403 <c>tournaments.forbidden</c>).
/// </remarks>
public sealed class GetTournamentEligibilityProfileHandler
    : IRequestHandler<GetTournamentEligibilityProfileQuery, Result<TournamentEligibilityProfileBody>>
{
    private readonly TournamentDrawScope _scope;
    private readonly IEligibilityProfileRepository _profiles;

    public GetTournamentEligibilityProfileHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        IEligibilityProfileRepository profiles)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
        _profiles = profiles;
    }

    public async Task<Result<TournamentEligibilityProfileBody>> Handle(
        GetTournamentEligibilityProfileQuery request,
        CancellationToken cancellationToken)
    {
        var tournament = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (tournament.IsFailure)
            return Result.Failure<TournamentEligibilityProfileBody>(tournament.Error);

        var profileId = await _profiles.GetTournamentProfileIdAsync(request.TournamentId, cancellationToken);
        return Result.Success(new TournamentEligibilityProfileBody(profileId?.Value));
    }
}

/// <remarks>
/// Order: tournament tenant rule, then <see cref="EligibilityProfileErrors.NotFound"/> when the profile is not one of the
/// tournament's organization (activity is not checked), then the write. A profile deleted before the write is also
/// <see cref="EligibilityProfileErrors.NotFound"/> and a tournament deleted before it <see cref="AdminTournamentErrors.NotFound"/>;
/// nothing is written on a failure.
/// </remarks>
public sealed class SetTournamentEligibilityProfileHandler
    : IRequestHandler<SetTournamentEligibilityProfileCommand, Result<TournamentEligibilityProfileBody>>
{
    private readonly TournamentDrawScope _scope;
    private readonly IEligibilityProfileRepository _profiles;

    public SetTournamentEligibilityProfileHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        IEligibilityProfileRepository profiles)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
        _profiles = profiles;
    }

    public async Task<Result<TournamentEligibilityProfileBody>> Handle(
        SetTournamentEligibilityProfileCommand request,
        CancellationToken cancellationToken)
    {
        var tournament = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (tournament.IsFailure)
            return Result.Failure<TournamentEligibilityProfileBody>(tournament.Error);

        EligibilityProfileId? profileId = null;
        if (request.EligibilityProfileId is { } requested)
        {
            var profile = await _profiles.GetByIdAsync(
                new OrganizationId(tournament.Value.OrganizationId), new EligibilityProfileId(requested), cancellationToken);
            if (profile is null)
                return Result.Failure<TournamentEligibilityProfileBody>(EligibilityProfileErrors.NotFound);
            profileId = profile.Id;
        }

        var written = await PersistenceWrites.TryAsync(
            () => _profiles.SetTournamentProfileIdAsync(request.TournamentId, profileId, cancellationToken));
        if (written.IsFailure)
            return Result.Failure<TournamentEligibilityProfileBody>(written.Error);

        return written.Value
            ? Result.Success(new TournamentEligibilityProfileBody(profileId?.Value))
            : Result.Failure<TournamentEligibilityProfileBody>(AdminTournamentErrors.NotFound);
    }
}
