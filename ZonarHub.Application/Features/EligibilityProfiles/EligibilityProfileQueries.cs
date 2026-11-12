using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Registrations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.EligibilityProfiles;

/// <summary>The organization's eligibility profiles with their slots, ordered by name.</summary>
public sealed record ListEligibilityProfilesQuery(Guid OrganizationId) : IRequest<Result<IReadOnlyList<EligibilityProfileResponse>>>;

/// <summary>The organization's profile; <see cref="EligibilityProfileErrors.NotFound"/> also for another organization's.</summary>
public sealed record GetEligibilityProfileQuery(Guid OrganizationId, Guid ProfileId) : IRequest<Result<EligibilityProfileResponse>>;

/// <remarks>Tenant rule of <see cref="TournamentOrganizationAccess"/> first (<see cref="AdminTournamentErrors.Forbidden"/>).</remarks>
public sealed class ListEligibilityProfilesHandler
    : IRequestHandler<ListEligibilityProfilesQuery, Result<IReadOnlyList<EligibilityProfileResponse>>>
{
    private readonly TournamentOrganizationAccess _access;
    private readonly IEligibilityProfileRepository _profiles;

    public ListEligibilityProfilesHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        IEligibilityProfileRepository profiles)
    {
        _access = new TournamentOrganizationAccess(currentUser, users, organizations);
        _profiles = profiles;
    }

    public async Task<Result<IReadOnlyList<EligibilityProfileResponse>>> Handle(
        ListEligibilityProfilesQuery request,
        CancellationToken cancellationToken)
    {
        if (!await _access.CanAccessAsync(request.OrganizationId, cancellationToken))
            return Result.Failure<IReadOnlyList<EligibilityProfileResponse>>(AdminTournamentErrors.Forbidden);

        var profiles = await _profiles.ListByOrganizationAsync(new OrganizationId(request.OrganizationId), cancellationToken);
        return Result.Success<IReadOnlyList<EligibilityProfileResponse>>(profiles.Select(EligibilityProfileMapper.ToResponse).ToList());
    }
}

/// <remarks>Tenant rule of <see cref="TournamentOrganizationAccess"/> first (<see cref="AdminTournamentErrors.Forbidden"/>).</remarks>
public sealed class GetEligibilityProfileHandler : IRequestHandler<GetEligibilityProfileQuery, Result<EligibilityProfileResponse>>
{
    private readonly TournamentOrganizationAccess _access;
    private readonly IEligibilityProfileRepository _profiles;

    public GetEligibilityProfileHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        IEligibilityProfileRepository profiles)
    {
        _access = new TournamentOrganizationAccess(currentUser, users, organizations);
        _profiles = profiles;
    }

    public async Task<Result<EligibilityProfileResponse>> Handle(GetEligibilityProfileQuery request, CancellationToken cancellationToken)
    {
        if (!await _access.CanAccessAsync(request.OrganizationId, cancellationToken))
            return Result.Failure<EligibilityProfileResponse>(AdminTournamentErrors.Forbidden);

        var profile = await _profiles.GetByIdAsync(
            new OrganizationId(request.OrganizationId), new EligibilityProfileId(request.ProfileId), cancellationToken);
        return profile is null
            ? Result.Failure<EligibilityProfileResponse>(EligibilityProfileErrors.NotFound)
            : Result.Success(EligibilityProfileMapper.ToResponse(profile));
    }
}
