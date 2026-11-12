using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.TournamentRegistrations;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Registrations;

namespace ZonarHub.Application.Features.PublicRegistrations;

/// <summary>The registration form data of a showcase tournament (D4).</summary>
public sealed record GetPublicRegistrationInfoQuery(string Slug) : IRequest<Result<PublicRegistrationInfoResponse>>;

/// <remarks>
/// The slots are those of <see cref="TournamentEligibilitySlots"/> (the active profile, else the tournament's own rules;
/// a referenced profile of another organization is <see cref="EligibilityProfileErrors.NotFound"/>). Places left count
/// confirmed and pending registrations, the ones a new submission waits behind (D2).
/// </remarks>
public sealed class GetPublicRegistrationInfoHandler
    : IRequestHandler<GetPublicRegistrationInfoQuery, Result<PublicRegistrationInfoResponse>>
{
    private readonly PublicRegistrationScope _scope;
    private readonly ITournamentRegistrationRepository _registrations;
    private readonly TournamentEligibilitySlots _slots;
    private readonly IClock _clock;

    public GetPublicRegistrationInfoHandler(
        IPublicTournamentReadRepository showcase,
        ITournamentAdminRepository tournaments,
        ITournamentRegistrationRepository registrations,
        IEligibilityProfileRepository profiles,
        ITournamentModalityRepository modalities,
        IClock clock)
    {
        _scope = new PublicRegistrationScope(showcase, tournaments);
        _registrations = registrations;
        _slots = new TournamentEligibilitySlots(profiles, modalities);
        _clock = clock;
    }

    public async Task<Result<PublicRegistrationInfoResponse>> Handle(GetPublicRegistrationInfoQuery request, CancellationToken cancellationToken)
    {
        var loaded = await _scope.LoadAsync(request.Slug, cancellationToken);
        if (loaded.IsFailure)
            return Result.Failure<PublicRegistrationInfoResponse>(loaded.Error);

        var tournament = loaded.Value;
        var slots = await _slots.ResolveAsync(tournament, cancellationToken);
        if (slots.IsFailure)
            return Result.Failure<PublicRegistrationInfoResponse>(slots.Error);

        var capacity = new RegistrationCapacity(
            tournament.MaxPairs,
            await _registrations.CountAsync(tournament.Id, [RegistrationStatus.Confirmed], cancellationToken),
            await _registrations.CountAsync(tournament.Id, [RegistrationStatus.Pending], cancellationToken));
        var state = PublicRegistrationScope.WindowState(tournament, _clock.UtcNow);

        return Result.Success(new PublicRegistrationInfoResponse(
            request.Slug,
            StateName(state),
            state == RegistrationWindowState.Open,
            tournament.RegistrationStartDate,
            tournament.RegistrationEndDate,
            new PublicRegistrationCapacityResponse(
                capacity.MaxPairs is > 0 ? capacity.MaxPairs : null,
                capacity.IsLimited ? Math.Max(0, capacity.MaxPairs!.Value - capacity.Confirmed - capacity.Pending) : null,
                capacity.IsFullForNewEntries),
            new PublicRegistrationFeeResponse(
                tournament.RegistrationFeePerPair,
                RegistrationRules.InitialPaymentStatus(tournament.RegistrationFeePerPair) == RegistrationPaymentStatus.Pending),
            slots.Value
                .Select(s => new PublicRegistrationSlotResponse(s.SlotNumber, s.Label, s.Required, s.GenderId, s.CategoryId, s.MinAge, s.MaxAge))
                .ToList(),
            RegistrationEligibility.CategoryTolerance,
            PublicRegistrationKeys.ConsentText));
    }

    private static string StateName(RegistrationWindowState state) => state switch
    {
        RegistrationWindowState.NotStarted => "notStarted",
        RegistrationWindowState.Ended => "ended",
        _ => "open",
    };
}
