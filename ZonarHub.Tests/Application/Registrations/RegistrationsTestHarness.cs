using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.EligibilityProfiles;
using ZonarHub.Application.Features.Players;
using ZonarHub.Application.Features.TournamentRegistrations;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Registrations;
using ZonarHub.Domain.Tournaments;
using ZonarHub.Domain.Users;
using ZonarHub.Tests.Application.AdminTournaments;
using ZonarHub.Tests.Application.PublicTournaments;

namespace ZonarHub.Tests.Application.Registrations;

/// <summary>
/// Wires the real player, eligibility profile and registration handlers to the in-memory user/organization stores, the
/// recording tournament repository and in-memory stores that model the R1 store rules.
/// </summary>
internal sealed class RegistrationsTestHarness
{
    public static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    public static readonly Guid Male = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    public static readonly Guid Female = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    public static readonly Guid Mixed = Guid.Parse("a1000000-0000-0000-0000-000000000003");
    public static readonly Guid Third = Guid.Parse("b1000000-0000-0000-0000-000000000003");
    public static readonly Guid Fourth = Guid.Parse("b1000000-0000-0000-0000-000000000004");
    public static readonly Guid Sixth = Guid.Parse("b1000000-0000-0000-0000-000000000006");
    public static readonly Guid SingleModality = Guid.Parse("550e8400-e29b-41d4-a716-446655440001");
    public static readonly Guid DoublesModality = Guid.Parse("550e8400-e29b-41d4-a716-446655440002");

    public RegistrationsTestHarness()
    {
        Tournaments = new AdminTournamentsListTestHarness(Now);
        Players = new InMemoryPlayerRepository();
        Profiles = new InMemoryEligibilityProfileRepository();
        Registrations = new InMemoryTournamentRegistrationRepository(Players, Tournaments.Tournaments, Now);
        Genders.Items.AddRange([Gender(Male, "male"), Gender(Female, "female"), Gender(Mixed, "mixed")]);
        Categories.Items.AddRange([Category(Third, 3), Category(Fourth, 4), Category(Sixth, 6)]);
        Modalities.Items.AddRange([new(SingleModality, "Individual", "Single", "Individual", "single", 1, true), new(DoublesModality, "Parejas", "Doubles", "Duplas", "doubles", 2, true)]);

        var u = Tournaments.Users;
        SearchPlayers = new SearchPlayersHandler(u.CurrentUser, u.Users, u.Organizations, Players);
        GetPlayer = new GetPlayerHandler(u.CurrentUser, u.Users, u.Organizations, Players);
        CreatePlayer = new CreatePlayerHandler(u.CurrentUser, u.Users, u.Organizations, Players, Genders, Categories, u.Clock);
        UpdatePlayer = new UpdatePlayerHandler(u.CurrentUser, u.Users, u.Organizations, Players, Genders, Categories, u.Clock);
        ListProfiles = new ListEligibilityProfilesHandler(u.CurrentUser, u.Users, u.Organizations, Profiles);
        GetProfile = new GetEligibilityProfileHandler(u.CurrentUser, u.Users, u.Organizations, Profiles);
        CreateProfile = new CreateEligibilityProfileHandler(u.CurrentUser, u.Users, u.Organizations, Profiles, Genders, Categories, u.Clock);
        UpdateProfile = new UpdateEligibilityProfileHandler(u.CurrentUser, u.Users, u.Organizations, Profiles, Genders, Categories, u.Clock);
        DeleteProfile = new DeleteEligibilityProfileHandler(u.CurrentUser, u.Users, u.Organizations, Profiles);
        ListRegistrations = new ListTournamentRegistrationsHandler(u.CurrentUser, u.Users, u.Organizations, Tournaments.Tournaments, Registrations);
        GetRegistration = new GetTournamentRegistrationHandler(u.CurrentUser, u.Users, u.Organizations, Tournaments.Tournaments, Registrations);
        CreateRegistration = new CreateTournamentRegistrationHandler(
            u.CurrentUser, u.Users, u.Organizations, Tournaments.Tournaments, Registrations, Players, Profiles, Modalities, Genders, Categories, u.Clock);
        Transition = new TransitionTournamentRegistrationHandler(u.CurrentUser, u.Users, u.Organizations, Tournaments.Tournaments, Registrations);
        Payment = new SetTournamentRegistrationPaymentHandler(u.CurrentUser, u.Users, u.Organizations, Tournaments.Tournaments, Registrations);
        DrawParticipants = new ListRegistrationDrawParticipantsHandler(u.CurrentUser, u.Users, u.Organizations, Tournaments.Tournaments, Registrations);
        Sender = new HandlerSender(this);
    }

    public AdminTournamentsListTestHarness Tournaments { get; }
    public InMemoryPlayerRepository Players { get; }
    public InMemoryEligibilityProfileRepository Profiles { get; }
    public InMemoryTournamentRegistrationRepository Registrations { get; }
    public FakeGenderRepository Genders { get; } = new();
    public FakeCategoryRepository Categories { get; } = new();
    public InMemoryModalityRepository Modalities { get; } = new();
    public SearchPlayersHandler SearchPlayers { get; }
    public GetPlayerHandler GetPlayer { get; }
    public CreatePlayerHandler CreatePlayer { get; }
    public UpdatePlayerHandler UpdatePlayer { get; }
    public ListEligibilityProfilesHandler ListProfiles { get; }
    public GetEligibilityProfileHandler GetProfile { get; }
    public CreateEligibilityProfileHandler CreateProfile { get; }
    public UpdateEligibilityProfileHandler UpdateProfile { get; }
    public DeleteEligibilityProfileHandler DeleteProfile { get; }
    public ListTournamentRegistrationsHandler ListRegistrations { get; }
    public GetTournamentRegistrationHandler GetRegistration { get; }
    public CreateTournamentRegistrationHandler CreateRegistration { get; }
    public TransitionTournamentRegistrationHandler Transition { get; }
    public SetTournamentRegistrationPaymentHandler Payment { get; }
    public ListRegistrationDrawParticipantsHandler DrawParticipants { get; }
    public ISender Sender { get; }

    /// <summary>An organization of the caller's own tenant (admin caller).</summary>
    public async Task<Guid> OwnOrganizationAsync()
    {
        var tenant = Guid.NewGuid();
        var org = await Tournaments.Users.SeedOrganizationAsync(tenant, "Org A");
        await AdminTournamentsItemAccessTests.AuthenticateAsync(Tournaments, UserRole.Admin, tenant);
        return org.Id.Value;
    }

    /// <summary>An organization of another tenant; the caller is an admin of a different tenant.</summary>
    public async Task<Guid> ForeignOrganizationAsync()
    {
        var org = await Tournaments.Users.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        await AdminTournamentsItemAccessTests.AuthenticateAsync(Tournaments, UserRole.Admin, Guid.NewGuid());
        return org.Id.Value;
    }

    /// <summary>A doubles tournament of <paramref name="organizationId"/> without gender or category rules.</summary>
    public async Task<TournamentAdminDto> TournamentAsync(
        Guid organizationId,
        int? maxPairs = null,
        Guid? modalityId = null,
        Guid? genderId = null,
        Guid? categoryId = null,
        decimal? fee = null)
    {
        var seeded = Tournaments.Tournaments.Seed(organizationId, "Open A");
        return await Tournaments.Tournaments.UpdateAsync(seeded with
        {
            MaxPairs = maxPairs,
            ModalityId = modalityId ?? DoublesModality,
            GenderId = genderId,
            CategoryId = categoryId,
            RegistrationFeePerPair = fee,
        });
    }

    public Player SeedPlayer(
        Guid organizationId,
        string firstName,
        string lastName,
        string? club = null,
        Guid? gender = null,
        Guid? category = null,
        DateOnly? birthDate = null,
        string? email = null,
        bool isActive = true)
    {
        var player = Player.Reconstitute(
            PlayerId.New(),
            new OrganizationId(organizationId),
            null,
            new PlayerDetails(firstName, lastName, email, null, null, birthDate, gender, category, club),
            isActive,
            Now,
            Now);
        Players.Seed(player);
        return player;
    }

    /// <summary>Two players of <paramref name="organizationId"/> registered at <paramref name="status"/> (seeded, not a request write).</summary>
    public TournamentRegistrationDetails SeedRegistration(
        TournamentAdminDto tournament,
        RegistrationStatus status,
        params Player[] players)
    {
        var members = players.Length > 0
            ? players
            : [SeedPlayer(tournament.OrganizationId, "Ana", $"Gil {Guid.NewGuid():N}"), SeedPlayer(tournament.OrganizationId, "Bea", $"Paz {Guid.NewGuid():N}")];
        return Registrations.Seed(tournament, status, members);
    }

    private static GenderDto Gender(Guid id, string key) => new(id, key, key, true, 1, Now, Now);

    private static CategoryDto Category(Guid id, int level) => new(id, $"{level}ª", $"{level}ª", $"c{level}", level, true, level, Now, Now);

    /// <summary>Routes each request to the matching real handler.</summary>
    private sealed class HandlerSender(RegistrationsTestHarness h) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                SearchPlayersQuery query => await h.SearchPlayers.Handle(query, cancellationToken),
                GetPlayerQuery query => await h.GetPlayer.Handle(query, cancellationToken),
                CreatePlayerCommand command => await h.CreatePlayer.Handle(command, cancellationToken),
                UpdatePlayerCommand command => await h.UpdatePlayer.Handle(command, cancellationToken),
                ListEligibilityProfilesQuery query => await h.ListProfiles.Handle(query, cancellationToken),
                GetEligibilityProfileQuery query => await h.GetProfile.Handle(query, cancellationToken),
                CreateEligibilityProfileCommand command => await h.CreateProfile.Handle(command, cancellationToken),
                UpdateEligibilityProfileCommand command => await h.UpdateProfile.Handle(command, cancellationToken),
                DeleteEligibilityProfileCommand command => await h.DeleteProfile.Handle(command, cancellationToken),
                ListTournamentRegistrationsQuery query => await h.ListRegistrations.Handle(query, cancellationToken),
                GetTournamentRegistrationQuery query => await h.GetRegistration.Handle(query, cancellationToken),
                CreateTournamentRegistrationCommand command => await h.CreateRegistration.Handle(command, cancellationToken),
                TransitionTournamentRegistrationCommand command => await h.Transition.Handle(command, cancellationToken),
                SetTournamentRegistrationPaymentCommand command => await h.Payment.Handle(command, cancellationToken),
                ListRegistrationDrawParticipantsQuery query => await h.DrawParticipants.Handle(query, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name),
            };
            return (TResponse)result;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

/// <summary>Modalities by id, as the catalog repository answers them.</summary>
internal sealed class InMemoryModalityRepository : ITournamentModalityRepository
{
    public List<TournamentModalityDto> Items { get; } = [];

    public Task<TournamentModalityDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.SingleOrDefault(m => m.Id == id));

    public Task<IReadOnlyList<TournamentModalityDto>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<TournamentModalityDto>> GetAllActiveAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TournamentModalityDto?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TournamentModalityDto> AddAsync(TournamentModalityDto modality, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TournamentModalityDto> UpdateAsync(TournamentModalityDto modality, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>
/// Players by id with the store's unique rules (email lowercase and document per organization). <see cref="Writes"/>
/// lists accepted writes only.
/// </summary>
internal sealed class InMemoryPlayerRepository : IPlayerRepository
{
    private readonly Dictionary<PlayerId, Player> _players = [];

    public List<string> Writes { get; } = [];

    public void Seed(Player player) => _players[player.Id] = player;

    public Player? Find(PlayerId id) => _players.GetValueOrDefault(id);

    public Task<Player?> GetByIdAsync(OrganizationId organizationId, PlayerId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_players.TryGetValue(id, out var player) && player.OrganizationId == organizationId ? Copy(player) : null);

    public Task<IReadOnlyList<Player>> GetByIdsAsync(OrganizationId organizationId, IReadOnlyCollection<PlayerId> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Player>>(ids.Distinct()
            .Select(id => _players.GetValueOrDefault(id))
            .Where(p => p is not null && p.OrganizationId == organizationId)
            .Select(p => Copy(p!))
            .ToList());

    public Task<PageResult<Player>> SearchAsync(PlayerSearch search, CancellationToken cancellationToken = default)
    {
        var paging = new PageRequest(search.Page, search.PageSize).Normalize();
        var text = search.Text?.Trim();
        var matches = _players.Values
            .Where(p => p.OrganizationId == search.OrganizationId)
            .Where(p => search.IsActive is null || p.IsActive == search.IsActive)
            .Where(p => string.IsNullOrEmpty(text)
                || new[] { p.FirstName, p.LastName, p.Email, p.DocumentId }.Any(v => v?.Contains(text, StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(p => p.LastName, StringComparer.Ordinal).ThenBy(p => p.FirstName, StringComparer.Ordinal).ThenBy(p => p.Id.Value)
            .ToList();
        return Task.FromResult(new PageResult<Player>(
            matches.Skip(paging.Skip).Take(paging.PageSize).Select(Copy).ToList(), paging.Page, paging.PageSize, matches.Count));
    }

    public Task<Player?> FindByEmailAsync(OrganizationId organizationId, string email, CancellationToken cancellationToken = default) =>
        Task.FromResult(Player.NormalizeEmail(email) is { } normalized
            && _players.Values.SingleOrDefault(p => p.OrganizationId == organizationId && p.Email == normalized) is { } player
                ? Copy(player)
                : null);

    public Task<Player?> FindByDocumentAsync(OrganizationId organizationId, string documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(documentId)
            && _players.Values.SingleOrDefault(p => p.OrganizationId == organizationId && p.DocumentId == documentId.Trim()) is { } player
                ? Copy(player)
                : null);

    public Task AddAsync(Player player, CancellationToken cancellationToken = default)
    {
        CheckUnique(player);
        Writes.Add($"add:{player.Id}");
        _players[player.Id] = player;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Player player, CancellationToken cancellationToken = default)
    {
        if (!_players.TryGetValue(player.Id, out var stored) || stored.OrganizationId != player.OrganizationId)
            throw new PersistenceConflictException(PlayerErrors.NotFound);

        CheckUnique(player);
        Writes.Add($"update:{player.Id}");
        _players[player.Id] = player;
        return Task.CompletedTask;
    }

    /// <summary>A detached copy, as a store read returns: changing it does not change the stored row.</summary>
    private static Player Copy(Player p) => Player.Reconstitute(
        p.Id, p.OrganizationId, p.UserId,
        new PlayerDetails(p.FirstName, p.LastName, p.Email, p.Phone, p.DocumentId, p.BirthDate, p.GenderId, p.CategoryId, p.Club),
        p.IsActive, p.CreatedAtUtc, p.UpdatedAtUtc);

    private void CheckUnique(Player player)
    {
        var others = _players.Values.Where(p => p.OrganizationId == player.OrganizationId && p.Id != player.Id).ToList();
        if (player.Email is not null && others.Any(p => p.Email == player.Email))
            throw new PersistenceConflictException(PlayerErrors.EmailTaken);
        if (player.DocumentId is not null && others.Any(p => p.DocumentId == player.DocumentId))
            throw new PersistenceConflictException(PlayerErrors.DocumentTaken);
    }
}

/// <summary>
/// Profiles by id with the store's unique name (any case) per organization, the tournament references of
/// <c>tournaments.eligibility_profile_id</c> and its delete restriction.
/// </summary>
internal sealed class InMemoryEligibilityProfileRepository : IEligibilityProfileRepository
{
    private readonly Dictionary<EligibilityProfileId, EligibilityProfile> _profiles = [];

    public Dictionary<Guid, EligibilityProfileId> TournamentProfiles { get; } = [];

    public List<string> Writes { get; } = [];

    public EligibilityProfile? Find(EligibilityProfileId id) => _profiles.GetValueOrDefault(id);

    public void Seed(EligibilityProfile profile) => _profiles[profile.Id] = profile;

    public Task<IReadOnlyList<EligibilityProfile>> ListByOrganizationAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EligibilityProfile>>(_profiles.Values
            .Where(p => p.OrganizationId == organizationId)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList());

    public Task<EligibilityProfile?> GetByIdAsync(OrganizationId organizationId, EligibilityProfileId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_profiles.TryGetValue(id, out var profile) && profile.OrganizationId == organizationId
            ? EligibilityProfile.Reconstitute(profile.Id, profile.OrganizationId, profile.Name, profile.Description, profile.IsActive, profile.Slots, profile.CreatedAtUtc, profile.UpdatedAtUtc)
            : null);

    public Task<EligibilityProfileId?> GetTournamentProfileIdAsync(Guid tournamentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(TournamentProfiles.TryGetValue(tournamentId, out var id) ? id : (EligibilityProfileId?)null);

    public Task<EligibilityProfile> SaveAsync(EligibilityProfile profile, CancellationToken cancellationToken = default)
    {
        if (_profiles.TryGetValue(profile.Id, out var stored) && stored.OrganizationId != profile.OrganizationId)
            throw new PersistenceConflictException(EligibilityProfileErrors.NotFound);
        if (_profiles.Values.Any(p => p.OrganizationId == profile.OrganizationId && p.Id != profile.Id
                && string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
            throw new PersistenceConflictException(EligibilityProfileErrors.NameTaken);

        Writes.Add($"save:{profile.Id}");
        _profiles[profile.Id] = profile;
        return Task.FromResult(profile);
    }

    public Task<bool> DeleteAsync(OrganizationId organizationId, EligibilityProfileId id, CancellationToken cancellationToken = default)
    {
        if (!_profiles.TryGetValue(id, out var stored) || stored.OrganizationId != organizationId)
            return Task.FromResult(false);
        if (TournamentProfiles.ContainsValue(id))
            throw new PersistenceConflictException(EligibilityProfileErrors.InUse);

        Writes.Add($"delete:{id}");
        _profiles.Remove(id);
        return Task.FromResult(true);
    }
}

/// <summary>
/// Registrations modelling the R1 store functions: every write locks nothing but re-applies the D2 rules on the current
/// rows (organization of the players, one active registration per player, capacity, expected revision) and bumps the
/// revision. <see cref="BeforeNextWrite"/> commits a concurrent change between the caller's read and its write.
/// <see cref="Writes"/> lists accepted writes, <see cref="RejectedWrites"/> the refused ones.
/// </summary>
internal sealed class InMemoryTournamentRegistrationRepository(
    InMemoryPlayerRepository players,
    RecordingTournamentAdminRepository tournaments,
    DateTime nowUtc) : ITournamentRegistrationRepository
{
    private readonly List<TournamentRegistration> _rows = [];

    public List<string> Writes { get; } = [];

    public List<(string Write, string Code)> RejectedWrites { get; } = [];

    public List<RegistrationRequest> CreatedRequests { get; } = [];

    public Action? BeforeNextWrite { get; set; }

    /// <summary>Awaited once by the next tournament listing, so a test can hold one request mid-flight.</summary>
    public Func<Task>? BeforeNextList { get; set; }

    public TournamentRegistration? Find(Guid id) => _rows.SingleOrDefault(r => r.Id.Value == id);

    public TournamentRegistrationDetails Seed(TournamentAdminDto tournament, RegistrationStatus status, IReadOnlyList<Player> members)
    {
        var registration = TournamentRegistration.Reconstitute(
            TournamentRegistrationId.New(), new TournamentId(tournament.Id), new OrganizationId(tournament.OrganizationId),
            status, RegistrationSource.Admin, RegistrationPaymentStatus.NotRequired, null, null, false,
            members.Select((p, i) => new RegistrationParticipant(i + 1, p.Id)).ToList(), 0, nowUtc.AddMinutes(_rows.Count), nowUtc);
        _rows.Add(registration);
        return Details(registration);
    }

    /// <summary>Replaces a stored registration at the next revision, as another writer would.</summary>
    public void SeedChange(Guid id, RegistrationStatus? status = null, RegistrationPaymentStatus? payment = null)
    {
        var r = Find(id)!;
        Replace(r, status ?? r.Status, payment ?? r.PaymentStatus);
    }

    public async Task<IReadOnlyList<TournamentRegistrationDetails>> ListByTournamentAsync(Guid tournamentId, RegistrationStatus? status = null, CancellationToken cancellationToken = default)
    {
        var gate = BeforeNextList;
        BeforeNextList = null;
        if (gate is not null)
            await gate();

        return _rows
            .Where(r => r.TournamentId.Value == tournamentId && (status is null || r.Status == status))
            .OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Id.Value)
            .Select(Details)
            .ToList();
    }

    public Task<TournamentRegistrationDetails?> GetAsync(Guid tournamentId, TournamentRegistrationId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_rows.SingleOrDefault(r => r.Id == id && r.TournamentId.Value == tournamentId) is { } r ? Details(r) : null);

    public async Task<TournamentRegistrationDetails> CreateAsync(RegistrationRequest request, CancellationToken cancellationToken = default)
    {
        RunConcurrentWrite();
        var tournament = await tournaments.GetByIdAsync(request.TournamentId.Value, cancellationToken);
        if (tournament is null)
            Reject("create", RegistrationErrors.TournamentNotFound);
        var organization = new OrganizationId(tournament!.OrganizationId);
        if (request.Participants.Any(p => players.Find(p.PlayerId) is not { } player || player.OrganizationId != organization))
            Reject("create", RegistrationErrors.PlayerNotFound);
        if (request.Participants.Any(p => _rows.Any(r => r.TournamentId == request.TournamentId && r.IsActive
                && r.Participants.Any(q => q.PlayerId == p.PlayerId))))
            Reject("create", RegistrationErrors.PlayerAlreadyRegistered);

        var admitted = RegistrationRules.Admit(request.RequestedStatus, Capacity(tournament));
        if (admitted.IsFailure)
            Reject("create", admitted.Error);

        var registration = TournamentRegistration.Reconstitute(
            TournamentRegistrationId.New(), request.TournamentId, organization, admitted.Value, request.Source, request.PaymentStatus,
            request.ContactEmail, request.Notes, request.PublicNameConsent, request.Participants, 0, nowUtc.AddMinutes(_rows.Count), nowUtc);
        _rows.Add(registration);
        CreatedRequests.Add(request);
        Writes.Add("create");
        return Details(registration);
    }

    public async Task<TournamentRegistrationDetails> TransitionAsync(TournamentRegistrationVersion expected, RegistrationStatus status, CancellationToken cancellationToken = default)
    {
        var write = $"status:{status}";
        var stored = Expect(write, expected);
        var tournament = await tournaments.GetByIdAsync(stored.TournamentId.Value, cancellationToken);
        var check = stored.CheckTransition(status, Capacity(tournament!));
        if (check.IsFailure)
            Reject(write, check.Error);

        Writes.Add(write);
        return Details(Replace(stored, status, stored.PaymentStatus));
    }

    public Task<TournamentRegistrationDetails> SetPaymentStatusAsync(TournamentRegistrationVersion expected, RegistrationPaymentStatus paymentStatus, CancellationToken cancellationToken = default)
    {
        var write = $"payment:{paymentStatus}";
        var stored = Expect(write, expected);
        Writes.Add(write);
        return Task.FromResult(Details(Replace(stored, stored.Status, paymentStatus)));
    }

    public Task<int> CountAsync(Guid tournamentId, IReadOnlyCollection<RegistrationStatus> statuses, CancellationToken cancellationToken = default) =>
        Task.FromResult(_rows.Count(r => r.TournamentId.Value == tournamentId && (statuses.Count == 0 || statuses.Contains(r.Status))));

    private RegistrationCapacity Capacity(TournamentAdminDto tournament) => new(
        tournament.MaxPairs,
        _rows.Count(r => r.TournamentId.Value == tournament.Id && r.Status == RegistrationStatus.Confirmed),
        _rows.Count(r => r.TournamentId.Value == tournament.Id && r.Status == RegistrationStatus.Pending));

    private TournamentRegistration Expect(string write, TournamentRegistrationVersion expected)
    {
        RunConcurrentWrite();
        var stored = _rows.SingleOrDefault(r => r.Id == expected.RegistrationId);
        if (stored is null || stored.Revision != expected.Revision)
            Reject(write, RegistrationErrors.ConcurrentModification);
        return stored!;
    }

    private void RunConcurrentWrite()
    {
        var concurrent = BeforeNextWrite;
        BeforeNextWrite = null;
        concurrent?.Invoke();
    }

    private void Reject(string write, ZonarHub.Domain.Common.Error error)
    {
        RejectedWrites.Add((write, error.Code));
        throw new PersistenceConflictException(error);
    }

    private TournamentRegistration Replace(TournamentRegistration r, RegistrationStatus status, RegistrationPaymentStatus payment)
    {
        var updated = TournamentRegistration.Reconstitute(
            r.Id, r.TournamentId, r.OrganizationId, status, r.Source, payment, r.ContactEmail, r.Notes, r.PublicNameConsent,
            r.Participants, r.Revision + 1, r.CreatedAtUtc, nowUtc);
        _rows[_rows.IndexOf(r)] = updated;
        return updated;
    }

    private TournamentRegistrationDetails Details(TournamentRegistration r) => new(
        r,
        r.Participants.Select(p =>
        {
            var player = players.Find(p.PlayerId)!;
            return new RegisteredParticipant(
                p.SlotNumber, p.PlayerId, player.FirstName, player.LastName, player.Club, player.GenderId, player.CategoryId, player.BirthDate);
        }).ToList());
}
