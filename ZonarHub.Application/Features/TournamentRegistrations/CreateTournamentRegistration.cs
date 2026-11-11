using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.EligibilityProfiles;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Registrations;
using ZonarHub.Domain.Tournaments;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.TournamentRegistrations;

/// <summary>Registers players of the tournament's organization from the backoffice (S4, D5).</summary>
public sealed record CreateTournamentRegistrationCommand(Guid TournamentId, CreateRegistrationBody Body)
    : IRequest<Result<TournamentRegistrationResponse>>;

/// <remarks>
/// Order, nothing written before the store call: tenant rule; requested status (<see cref="RegistrationErrors.InvalidStatus"/>
/// unless pending or confirmed); the <see cref="RegistrationRequest"/> rules; every player an active player of the
/// organization (<see cref="RegistrationErrors.PlayerNotFound"/>); D3 eligibility (<see cref="RegistrationEligibility"/>)
/// against the slots of <see cref="TournamentEligibilitySlots"/> (the active profile, else the tournament's own rules). The store then decides the status
/// (pending may become waitlisted) and refuses <see cref="RegistrationErrors.PlayerAlreadyRegistered"/> and
/// <see cref="RegistrationErrors.CapacityReached"/>. The payment status starts pending when the tournament has a fee.
/// </remarks>
public sealed class CreateTournamentRegistrationHandler
    : IRequestHandler<CreateTournamentRegistrationCommand, Result<TournamentRegistrationResponse>>
{
    private readonly TournamentDrawScope _scope;
    private readonly ITournamentRegistrationRepository _registrations;
    private readonly IPlayerRepository _players;
    private readonly TournamentEligibilitySlots _slots;
    private readonly CatalogReferences _catalogs;
    private readonly IClock _clock;

    public CreateTournamentRegistrationHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        ITournamentRegistrationRepository registrations,
        IPlayerRepository players,
        IEligibilityProfileRepository profiles,
        ITournamentModalityRepository modalities,
        IGenderRepository genders,
        ICategoryRepository categories,
        IClock clock)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
        _registrations = registrations;
        _players = players;
        _slots = new TournamentEligibilitySlots(profiles, modalities);
        _catalogs = new CatalogReferences(genders, categories);
        _clock = clock;
    }

    public async Task<Result<TournamentRegistrationResponse>> Handle(
        CreateTournamentRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var loaded = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (loaded.IsFailure)
            return Failure(loaded.Error);

        var tournament = loaded.Value;
        var body = request.Body;
        var status = body.Status ?? RegistrationApiNames.Name(RegistrationStatus.Pending);
        if (!RegistrationApiNames.TryParse(status, out RegistrationStatus requested)
            || requested is not (RegistrationStatus.Pending or RegistrationStatus.Confirmed))
            return Failure(RegistrationErrors.InvalidStatus);

        var built = RegistrationRequest.Create(
            new TournamentId(tournament.Id),
            RegistrationSource.Admin,
            requested,
            RegistrationRules.InitialPaymentStatus(tournament.RegistrationFeePerPair),
            body.ContactEmail,
            body.Notes,
            body.PublicNameConsent,
            (body.Participants ?? []).Select(p => new RegistrationParticipant(p.SlotNumber, new PlayerId(p.PlayerId))).ToList());
        if (built.IsFailure)
            return Failure(built.Error);

        var registration = built.Value;
        var organization = new OrganizationId(tournament.OrganizationId);
        var players = (await _players.GetByIdsAsync(organization, registration.Participants.Select(p => p.PlayerId).ToList(), cancellationToken))
            .Where(p => p.IsActive)
            .ToDictionary(p => p.Id);
        if (registration.Participants.Any(p => !players.ContainsKey(p.PlayerId)))
            return Failure(RegistrationErrors.PlayerNotFound);

        var slots = await _slots.ResolveAsync(tournament, cancellationToken);
        if (slots.IsFailure)
            return Failure(slots.Error);

        var catalog = await _catalogs.LoadAsync(cancellationToken);
        var eligible = RegistrationEligibility.Check(
            slots.Value,
            registration.Participants.Select(p =>
            {
                var player = players[p.PlayerId];
                return new EligibilityCandidate(p.SlotNumber, player.GenderId, player.CategoryId, player.BirthDate);
            }).ToList(),
            catalog.ToEligibilityCatalog(),
            DateOnly.FromDateTime(_clock.UtcNow));
        if (eligible.IsFailure)
            return Failure(eligible.Error);

        var created = await PersistenceWrites.TryAsync(() => _registrations.CreateAsync(registration, cancellationToken));
        return created.IsFailure ? Failure(created.Error) : Result.Success(TournamentRegistrationMapper.ToResponse(created.Value));
    }

    private static Result<TournamentRegistrationResponse> Failure(Error error) =>
        Result.Failure<TournamentRegistrationResponse>(error);
}
