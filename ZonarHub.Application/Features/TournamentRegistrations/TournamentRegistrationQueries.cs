using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Registrations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.TournamentRegistrations;

/// <summary>The tournament's registrations in creation order, optionally only those in <paramref name="Status"/>.</summary>
/// <param name="Status">An API status name (<see cref="RegistrationApiNames"/>) or null for all.</param>
public sealed record ListTournamentRegistrationsQuery(Guid TournamentId, string? Status)
    : IRequest<Result<TournamentRegistrationListResponse>>;

/// <summary>One registration of the tournament.</summary>
public sealed record GetTournamentRegistrationQuery(Guid TournamentId, Guid RegistrationId)
    : IRequest<Result<TournamentRegistrationResponse>>;

/// <summary>The confirmed registrations as draw participants (D5), in creation order.</summary>
public sealed record ListRegistrationDrawParticipantsQuery(Guid TournamentId) : IRequest<Result<IReadOnlyList<DrawParticipantBody>>>;

/// <remarks>
/// Every registration use case applies the tournament tenant rule of <see cref="TournamentDrawScope.LoadTournamentAsync"/>
/// first (404 <c>tournaments.not_found</c>, 403 <c>tournaments.forbidden</c>).
/// </remarks>
public sealed class ListTournamentRegistrationsHandler
    : IRequestHandler<ListTournamentRegistrationsQuery, Result<TournamentRegistrationListResponse>>
{
    private readonly TournamentDrawScope _scope;
    private readonly ITournamentRegistrationRepository _registrations;

    public ListTournamentRegistrationsHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        ITournamentRegistrationRepository registrations)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
        _registrations = registrations;
    }

    public async Task<Result<TournamentRegistrationListResponse>> Handle(
        ListTournamentRegistrationsQuery request,
        CancellationToken cancellationToken)
    {
        var tournament = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (tournament.IsFailure)
            return Result.Failure<TournamentRegistrationListResponse>(tournament.Error);

        RegistrationStatus? status = null;
        if (request.Status is not null)
        {
            if (!RegistrationApiNames.TryParse(request.Status, out RegistrationStatus parsed))
                return Result.Failure<TournamentRegistrationListResponse>(RegistrationErrors.InvalidStatus);
            status = parsed;
        }

        // One read: the capacity counts every registration, whatever the filter.
        var all = await _registrations.ListByTournamentAsync(request.TournamentId, null, cancellationToken);
        int Count(RegistrationStatus s) => all.Count(d => d.Registration.Status == s);
        var capacity = new RegistrationCapacity(tournament.Value.MaxPairs, Count(RegistrationStatus.Confirmed), Count(RegistrationStatus.Pending));

        return Result.Success(new TournamentRegistrationListResponse(
            all.Where(d => status is null || d.Registration.Status == status).Select(TournamentRegistrationMapper.ToResponse).ToList(),
            new RegistrationCapacityResponse(
                capacity.MaxPairs, capacity.Confirmed, capacity.Pending, Count(RegistrationStatus.Waitlisted), capacity.PlacesLeft)));
    }
}

public sealed class GetTournamentRegistrationHandler
    : IRequestHandler<GetTournamentRegistrationQuery, Result<TournamentRegistrationResponse>>
{
    private readonly TournamentDrawScope _scope;
    private readonly ITournamentRegistrationRepository _registrations;

    public GetTournamentRegistrationHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        ITournamentRegistrationRepository registrations)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
        _registrations = registrations;
    }

    public async Task<Result<TournamentRegistrationResponse>> Handle(GetTournamentRegistrationQuery request, CancellationToken cancellationToken)
    {
        var tournament = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (tournament.IsFailure)
            return Result.Failure<TournamentRegistrationResponse>(tournament.Error);

        var details = await _registrations.GetAsync(request.TournamentId, new TournamentRegistrationId(request.RegistrationId), cancellationToken);
        return details is null
            ? Result.Failure<TournamentRegistrationResponse>(RegistrationErrors.NotFound)
            : Result.Success(TournamentRegistrationMapper.ToResponse(details));
    }
}

/// <remarks>Mapping in <see cref="TournamentRegistrationMapper.ToDrawParticipant"/>; ready to send as draw participants.</remarks>
public sealed class ListRegistrationDrawParticipantsHandler
    : IRequestHandler<ListRegistrationDrawParticipantsQuery, Result<IReadOnlyList<DrawParticipantBody>>>
{
    private readonly TournamentDrawScope _scope;
    private readonly ITournamentRegistrationRepository _registrations;

    public ListRegistrationDrawParticipantsHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        ITournamentRegistrationRepository registrations)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
        _registrations = registrations;
    }

    public async Task<Result<IReadOnlyList<DrawParticipantBody>>> Handle(
        ListRegistrationDrawParticipantsQuery request,
        CancellationToken cancellationToken)
    {
        var tournament = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (tournament.IsFailure)
            return Result.Failure<IReadOnlyList<DrawParticipantBody>>(tournament.Error);

        var confirmed = await _registrations.ListByTournamentAsync(request.TournamentId, RegistrationStatus.Confirmed, cancellationToken);
        return Result.Success<IReadOnlyList<DrawParticipantBody>>(confirmed.Select(TournamentRegistrationMapper.ToDrawParticipant).ToList());
    }
}
