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
/// <item>the typed slots: the <see cref="RegistrationRequest"/> rules, then every filled slot exists and every required
/// slot is filled (<see cref="RegistrationErrors.InvalidSlot"/>, <see cref="RegistrationErrors.RequiredSlotMissing"/>);</item>
/// <item>each participant is the organization's player with its email, else with its document, else a new player. The
/// lookups and the tournament's registrations are read on every request, matched or not;</item>
/// <item>D3 eligibility of the new players' typed data, with its specific errors; missing data fails the rule that needs
/// it (L8);</item>
/// <item>the matched players (L9): any refusal that depends on a stored player (email and document of two players, a
/// deactivated player, one player in two slots, a player in another active registration of the tournament, or its stored
/// data failing D3) is the single <see cref="PublicRegistrationErrors.ParticipantUnavailable"/>, whatever the cause.</item>
/// </list>
/// A matched player is reused as stored and never written from the public form (L9). Then the code is consumed, new
/// players are added and the registration created as <c>pending</c> (the store makes it <c>waitlisted</c> when confirmed
/// plus pending reach the places). A refusal of the store after the code was consumed returns its error; players written
/// before it remain (they are valid records).
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

        var eligibility = catalog.ToEligibilityCatalog();
        var today = DateOnly.FromDateTime(now);
        var shape = Request(tournament, email.Value, body.PublicNameConsent, typed.Value.Select(t => (t.SlotNumber, t.Player.Id)));
        if (shape.IsFailure)
            return Failure(shape.Error);

        var unrestricted = slots.Value.Select(s => s with { GenderId = null, CategoryId = null, MinAge = null, MaxAge = null }).ToList();
        var filled = RegistrationEligibility.Check(unrestricted, typed.Value.Select(t => Candidate(t.SlotNumber, t.Player)).ToList(), eligibility, today);
        if (filled.IsFailure)
            return Failure(filled.Error);

        var matches = await MatchAsync(organization, typed.Value, cancellationToken);
        var registered = await _registrations.ListByTournamentAsync(tournament.Id, null, cancellationToken);

        var fresh = CheckSlots(slots.Value, matches.Where(m => m.Existing is null).Select(m => Candidate(m.SlotNumber, m.Typed)), eligibility, today);
        if (fresh.IsFailure)
            return Failure(fresh.Error);

        if (!MatchedCanRegister(matches, registered, slots.Value, eligibility, today))
            return Failure(PublicRegistrationErrors.ParticipantUnavailable);

        // The shape passed and every player is distinct; this only guards the domain invariants.
        var built = Request(tournament, email.Value, body.PublicNameConsent, matches.Select(m => (m.SlotNumber, (m.Existing ?? m.Typed).Id)));
        if (built.IsFailure)
            return Failure(built.Error);

        await _codes.ConsumeAsync(tournament.Id, email.Value, cancellationToken);
        foreach (var match in matches.Where(m => m.Existing is null).OrderBy(m => m.SlotNumber))
        {
            var written = await PersistenceWrites.TryAsync(() => _players.AddAsync(match.Typed, cancellationToken));
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

    /// <summary>The organization's players with each participant's email and with its document; both lookups always run.</summary>
    private async Task<IReadOnlyList<ParticipantMatch>> MatchAsync(
        OrganizationId organization,
        IReadOnlyList<(int SlotNumber, Player Player)> typed,
        CancellationToken cancellationToken)
    {
        var matches = new List<ParticipantMatch>();
        foreach (var (slot, input) in typed)
        {
            var byEmail = input.Email is { } email ? await _players.FindByEmailAsync(organization, email, cancellationToken) : null;
            var byDocument = input.DocumentId is { } document ? await _players.FindByDocumentAsync(organization, document, cancellationToken) : null;
            matches.Add(new ParticipantMatch(slot, input, byEmail, byDocument));
        }

        return matches;
    }

    /// <summary>
    /// True when every matched player may join as stored: one player per participant, active, in no other active
    /// registration of the tournament and eligible with its stored data. The caller never learns which rule failed (L9).
    /// </summary>
    private static bool MatchedCanRegister(
        IReadOnlyList<ParticipantMatch> matches,
        IReadOnlyList<TournamentRegistrationDetails> registered,
        IReadOnlyList<EligibilitySlot> slots,
        EligibilityCatalog eligibility,
        DateOnly today)
    {
        var matched = matches.Where(m => m.Existing is not null).ToList();
        if (matched.Any(m => m.IsConflict || !m.Existing!.IsActive))
            return false;

        var ids = matched.Select(m => m.Existing!.Id).ToList();
        if (ids.Distinct().Count() != ids.Count)
            return false;

        if (registered.Any(d => d.Registration.IsActive && d.Registration.Participants.Any(p => ids.Contains(p.PlayerId))))
            return false;

        return CheckSlots(slots, matched.Select(m => Candidate(m.SlotNumber, m.Existing!)), eligibility, today).IsSuccess;
    }

    /// <summary>D3 rules of only the given participants' slots (the filled and required slots are checked beforehand).</summary>
    private static Result CheckSlots(
        IReadOnlyList<EligibilitySlot> slots,
        IEnumerable<EligibilityCandidate> candidates,
        EligibilityCatalog eligibility,
        DateOnly today)
    {
        var subset = candidates.ToList();
        var numbers = subset.Select(c => c.SlotNumber).ToHashSet();
        return RegistrationEligibility.Check(slots.Where(s => numbers.Contains(s.SlotNumber)).ToList(), subset, eligibility, today);
    }

    private static EligibilityCandidate Candidate(int slotNumber, Player player) =>
        new(slotNumber, player.GenderId, player.CategoryId, player.BirthDate);

    private static Result<RegistrationRequest> Request(
        TournamentAdminDto tournament,
        string contactEmail,
        bool publicNameConsent,
        IEnumerable<(int SlotNumber, PlayerId PlayerId)> participants) =>
        RegistrationRequest.Create(
            new TournamentId(tournament.Id),
            RegistrationSource.Public,
            RegistrationStatus.Pending,
            RegistrationRules.InitialPaymentStatus(tournament.RegistrationFeePerPair),
            contactEmail,
            null,
            publicNameConsent,
            participants.Select(p => new RegistrationParticipant(p.SlotNumber, p.PlayerId)).ToList());

    private static Result<PublicRegistrationSubmittedResponse> Failure(Error error) => Result.Failure<PublicRegistrationSubmittedResponse>(error);

    /// <summary>A typed participant and the organization's players with its email and with its document.</summary>
    private sealed record ParticipantMatch(int SlotNumber, Player Typed, Player? ByEmail, Player? ByDocument)
    {
        /// <summary>The stored player this participant is; null for a new player.</summary>
        public Player? Existing => ByEmail ?? ByDocument;

        /// <summary>The email and the document belong to two different players.</summary>
        public bool IsConflict => ByEmail is not null && ByDocument is not null && ByEmail.Id != ByDocument.Id;
    }
}
