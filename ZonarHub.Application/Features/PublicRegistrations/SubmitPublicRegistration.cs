using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.EligibilityProfiles;
using ZonarHub.Application.Features.TournamentRegistrations;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Registrations;
using ZonarHub.Domain.Tournaments;

namespace ZonarHub.Application.Features.PublicRegistrations;

/// <summary>Registers a pair (or the modality's participants) of a showcase tournament anonymously (S2, S4, D4).</summary>
public sealed record SubmitPublicRegistrationCommand(string Slug, PublicRegistrationBody Body)
    : IRequest<Result<PublicRegistrationSubmittedResponse>>;

/// <remarks>
/// Order; nothing is written and the code stays usable until every check passed:
/// <list type="number">
/// <item>showcase tournament; open window (<see cref="PublicRegistrationErrors.Closed"/>); its slots
/// (<see cref="TournamentEligibilitySlots"/>);</item>
/// <item>contact email; participants (<see cref="RegistrationErrors.ParticipantsRequired"/>), each valid as a new
/// <see cref="Player"/> with catalog references (<c>players.*</c>), no email or document repeated
/// (<see cref="RegistrationErrors.DuplicatePlayer"/>);</item>
/// <item>the code of the contact email for this tournament (<see cref="PublicRegistrationErrors.InvalidCode"/>);</item>
/// <item>each participant is the organization's player with its email, else with its document, else a new player
/// (<see cref="PublicRegistrationErrors.ParticipantConflict"/> when they are two players,
/// <see cref="PublicRegistrationErrors.ParticipantUnavailable"/> for a deactivated one); a reused player is only completed
/// (<see cref="Player.FillMissingFrom"/>);</item>
/// <item>the <see cref="RegistrationRequest"/> rules; no participant in another active registration of the tournament
/// (<see cref="RegistrationErrors.PlayerAlreadyRegistered"/>); D3 eligibility of the resulting player data, where missing
/// data fails the rule that needs it (L8).</item>
/// </list>
/// Then the code is consumed, new players are added, completed players updated and the registration created as
/// <c>pending</c> (the store makes it <c>waitlisted</c> when confirmed plus pending reach the places). A refusal of the
/// store after the code was consumed returns its error; players written before it remain (they are valid records).
/// </remarks>
public sealed class SubmitPublicRegistrationHandler
    : IRequestHandler<SubmitPublicRegistrationCommand, Result<PublicRegistrationSubmittedResponse>>
{
    private readonly PublicRegistrationScope _scope;
    private readonly ITournamentRegistrationRepository _registrations;
    private readonly IPlayerRepository _players;
    private readonly TournamentEligibilitySlots _slots;
    private readonly CatalogReferences _catalogs;
    private readonly PublicRegistrationCodes _codes;
    private readonly IClock _clock;

    public SubmitPublicRegistrationHandler(
        IPublicTournamentReadRepository showcase,
        ITournamentAdminRepository tournaments,
        ITournamentRegistrationRepository registrations,
        IPlayerRepository players,
        IEligibilityProfileRepository profiles,
        ITournamentModalityRepository modalities,
        IGenderRepository genders,
        ICategoryRepository categories,
        ICacheStore cache,
        IClock clock)
    {
        _scope = new PublicRegistrationScope(showcase, tournaments);
        _registrations = registrations;
        _players = players;
        _slots = new TournamentEligibilitySlots(profiles, modalities);
        _catalogs = new CatalogReferences(genders, categories);
        _codes = new PublicRegistrationCodes(cache, clock);
        _clock = clock;
    }

    public async Task<Result<PublicRegistrationSubmittedResponse>> Handle(SubmitPublicRegistrationCommand request, CancellationToken cancellationToken)
    {
        var loaded = await _scope.LoadAsync(request.Slug, cancellationToken);
        if (loaded.IsFailure)
            return Failure(loaded.Error);

        var tournament = loaded.Value;
        var now = _clock.UtcNow;
        if (PublicRegistrationScope.WindowState(tournament, now) != RegistrationWindowState.Open)
            return Failure(PublicRegistrationErrors.Closed);

        var slots = await _slots.ResolveAsync(tournament, cancellationToken);
        if (slots.IsFailure)
            return Failure(slots.Error);

        var body = request.Body;
        var email = SendPublicRegistrationCodeHandler.ContactEmail(body.ContactEmail);
        if (email.IsFailure)
            return Failure(email.Error);

        var organization = new OrganizationId(tournament.OrganizationId);
        var catalog = await _catalogs.LoadAsync(cancellationToken);
        var typed = Typed(body.Participants, organization, catalog, now);
        if (typed.IsFailure)
            return Failure(typed.Error);

        if (!await _codes.VerifyAsync(tournament.Id, email.Value, body.Code, cancellationToken))
            return Failure(PublicRegistrationErrors.InvalidCode);

        var resolved = await ResolveAsync(organization, typed.Value, now, cancellationToken);
        if (resolved.IsFailure)
            return Failure(resolved.Error);

        var participants = resolved.Value;
        var built = RegistrationRequest.Create(
            new TournamentId(tournament.Id),
            RegistrationSource.Public,
            RegistrationStatus.Pending,
            RegistrationRules.InitialPaymentStatus(tournament.RegistrationFeePerPair),
            email.Value,
            null,
            body.PublicNameConsent,
            participants.Select(p => new RegistrationParticipant(p.SlotNumber, p.Player.Id)).ToList());
        if (built.IsFailure)
            return Failure(built.Error);

        var reused = participants.Where(p => p.Write != PlayerWrite.Add).Select(p => p.Player.Id).ToHashSet();
        var active = await _registrations.ListByTournamentAsync(tournament.Id, null, cancellationToken);
        if (active.Any(d => d.Registration.IsActive && d.Registration.Participants.Any(p => reused.Contains(p.PlayerId))))
            return Failure(RegistrationErrors.PlayerAlreadyRegistered);

        var eligible = RegistrationEligibility.Check(
            slots.Value,
            participants.Select(p => new EligibilityCandidate(p.SlotNumber, p.Player.GenderId, p.Player.CategoryId, p.Player.BirthDate)).ToList(),
            catalog.ToEligibilityCatalog(),
            DateOnly.FromDateTime(now));
        if (eligible.IsFailure)
            return Failure(eligible.Error);

        await _codes.ConsumeAsync(tournament.Id, email.Value, cancellationToken);
        foreach (var participant in participants.OrderBy(p => p.SlotNumber))
        {
            var written = participant.Write switch
            {
                PlayerWrite.Add => await PersistenceWrites.TryAsync(() => _players.AddAsync(participant.Player, cancellationToken)),
                PlayerWrite.Update => await PersistenceWrites.TryAsync(() => _players.UpdateAsync(participant.Player, cancellationToken)),
                _ => Result.Success(),
            };
            if (written.IsFailure)
                return Failure(written.Error);
        }

        var created = await PersistenceWrites.TryAsync(() => _registrations.CreateAsync(built.Value, cancellationToken));
        if (created.IsFailure)
            return Failure(created.Error);

        var registration = created.Value.Registration;
        return Result.Success(new PublicRegistrationSubmittedResponse(
            registration.Id.Value.ToString(),
            RegistrationApiNames.Name(registration.Status),
            new PublicRegistrationPaymentResponse(
                RegistrationApiNames.Name(registration.PaymentStatus),
                registration.PaymentStatus == RegistrationPaymentStatus.NotRequired ? null : tournament.RegistrationFeePerPair)));
    }

    /// <summary>Each participant validated as a new player of the organization; no email or document twice.</summary>
    private static Result<IReadOnlyList<(int SlotNumber, Player Player)>> Typed(
        IReadOnlyList<PublicParticipantBody>? participants,
        OrganizationId organization,
        CatalogSnapshot catalog,
        DateTime now)
    {
        if (participants is null || participants.Count == 0 || participants.Any(p => p is null))
            return Result.Failure<IReadOnlyList<(int, Player)>>(RegistrationErrors.ParticipantsRequired);

        var typed = new List<(int, Player)>();
        foreach (var p in participants)
        {
            if (!catalog.HasGender(p.GenderId))
                return Result.Failure<IReadOnlyList<(int, Player)>>(PlayerErrors.UnknownGender);
            if (!catalog.HasCategory(p.CategoryId))
                return Result.Failure<IReadOnlyList<(int, Player)>>(PlayerErrors.UnknownCategory);

            var player = Player.Create(
                PlayerId.New(),
                organization,
                null,
                new PlayerDetails(p.FirstName!, p.LastName!, p.Email, p.Phone, p.DocumentId, p.BirthDate, p.GenderId, p.CategoryId, p.Club),
                now);
            if (player.IsFailure)
                return Result.Failure<IReadOnlyList<(int, Player)>>(player.Error);

            typed.Add((p.SlotNumber, player.Value));
        }

        var players = typed.Select(t => t.Item2).ToList();
        if (Repeats(players.Select(p => p.Email)) || Repeats(players.Select(p => p.DocumentId)))
            return Result.Failure<IReadOnlyList<(int, Player)>>(RegistrationErrors.DuplicatePlayer);

        return Result.Success<IReadOnlyList<(int, Player)>>(typed);

        static bool Repeats(IEnumerable<string?> values)
        {
            var present = values.OfType<string>().ToList();
            return present.Distinct(StringComparer.Ordinal).Count() != present.Count;
        }
    }

    /// <summary>The organization's player of each participant, completed with the typed data, or the typed new player.</summary>
    private async Task<Result<IReadOnlyList<ResolvedParticipant>>> ResolveAsync(
        OrganizationId organization,
        IReadOnlyList<(int SlotNumber, Player Player)> typed,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var resolved = new List<ResolvedParticipant>();
        foreach (var (slot, input) in typed)
        {
            var byEmail = input.Email is { } email ? await _players.FindByEmailAsync(organization, email, cancellationToken) : null;
            var byDocument = input.DocumentId is { } document ? await _players.FindByDocumentAsync(organization, document, cancellationToken) : null;
            if (byEmail is not null && byDocument is not null && byEmail.Id != byDocument.Id)
                return Result.Failure<IReadOnlyList<ResolvedParticipant>>(PublicRegistrationErrors.ParticipantConflict);

            var existing = byEmail ?? byDocument;
            if (existing is null)
            {
                resolved.Add(new ResolvedParticipant(slot, input, PlayerWrite.Add));
                continue;
            }

            if (!existing.IsActive)
                return Result.Failure<IReadOnlyList<ResolvedParticipant>>(PublicRegistrationErrors.ParticipantUnavailable);

            var completed = existing.FillMissingFrom(input, now);
            resolved.Add(new ResolvedParticipant(slot, existing, completed ? PlayerWrite.Update : PlayerWrite.None));
        }

        return Result.Success<IReadOnlyList<ResolvedParticipant>>(resolved);
    }

    private static Result<PublicRegistrationSubmittedResponse> Failure(Error error) => Result.Failure<PublicRegistrationSubmittedResponse>(error);

    private enum PlayerWrite
    {
        None,
        Add,
        Update,
    }

    private sealed record ResolvedParticipant(int SlotNumber, Player Player, PlayerWrite Write);
}
