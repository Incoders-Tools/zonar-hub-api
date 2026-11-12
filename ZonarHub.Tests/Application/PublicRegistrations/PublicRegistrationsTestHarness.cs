using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.PublicRegistrations;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Registrations;
using ZonarHub.Tests.Application.PublicTournaments;
using ZonarHub.Tests.Application.Registrations;

namespace ZonarHub.Tests.Application.PublicRegistrations;

/// <summary>
/// Wires the real public registration handlers to the registrations harness stores (tournaments, players, profiles,
/// registrations, catalogs), a showcase read that only holds the tournaments seeded as public, a recording cache and a
/// recording email outbox. Anonymous: no caller is authenticated.
/// </summary>
internal sealed class PublicRegistrationsTestHarness
{
    public const string Slug = "open-norte-abc123";
    public static readonly DateOnly Opens = new(2026, 11, 1);
    public static readonly DateOnly Closes = new(2026, 11, 15);

    public PublicRegistrationsTestHarness()
    {
        R = new RegistrationsTestHarness();
        var tournaments = R.Tournaments.Tournaments;
        Info = new GetPublicRegistrationInfoHandler(Showcase, tournaments, R.Registrations, R.Profiles, R.Modalities, Clock);
        SendCode = new SendPublicRegistrationCodeHandler(Showcase, tournaments, Cache, Email, Templates, Clock);
        Submit = new SubmitPublicRegistrationHandler(
            Showcase, tournaments, R.Registrations, R.Players, R.Profiles, R.Modalities, R.Genders, R.Categories, Cache, Clock);
        ConfirmedPairs = new GetPublicConfirmedPairsHandler(Showcase, tournaments, R.Registrations);
        Sender = new HandlerSender(this);
    }

    public RegistrationsTestHarness R { get; }
    public FakePublicTournamentReadRepository Showcase { get; } = new();
    public RecordingCacheStore Cache { get; } = new();
    public RecordingEmailService Email { get; } = new();
    public RecordingTemplateComposer Templates { get; } = new();
    public ZonarHub.Tests.Common.TestClock Clock => R.Tournaments.Users.Clock;
    public GetPublicRegistrationInfoHandler Info { get; }
    public SendPublicRegistrationCodeHandler SendCode { get; }
    public SubmitPublicRegistrationHandler Submit { get; }
    public GetPublicConfirmedPairsHandler ConfirmedPairs { get; }
    public ISender Sender { get; }

    public Guid Organization { get; } = Guid.NewGuid();

    /// <summary>
    /// A doubles tournament of <see cref="Organization"/> with registrations from <see cref="Opens"/> to <see cref="Closes"/>,
    /// listed in the showcase under <paramref name="slug"/> unless <paramref name="isPublic"/> is false.
    /// </summary>
    public async Task<TournamentAdminDto> TournamentAsync(
        string slug = Slug,
        bool isPublic = true,
        int? maxPairs = null,
        decimal? fee = null,
        Guid? genderId = null,
        Guid? categoryId = null,
        DateOnly? opens = null,
        DateOnly? closes = null,
        bool window = true)
    {
        var seeded = await R.TournamentAsync(Organization, maxPairs, genderId: genderId, categoryId: categoryId, fee: fee);
        var tournament = await R.Tournaments.Tournaments.UpdateAsync(seeded with
        {
            RegistrationStartDate = window ? opens ?? Opens : null,
            RegistrationEndDate = window ? closes ?? Closes : null,
            IsPublic = isPublic,
            PublicSlug = slug,
        });
        if (isPublic)
            Showcase.Items.Add(PublicTournamentsTestHarness.Record(slug, id: tournament.Id));
        return tournament;
    }

    public Player SeedPlayer(
        string firstName,
        string lastName,
        string? email = null,
        string? phone = null,
        string? documentId = null,
        DateOnly? birthDate = null,
        Guid? gender = null,
        Guid? category = null,
        string? club = null,
        bool isActive = true,
        Guid? organization = null)
    {
        var player = Player.Reconstitute(
            PlayerId.New(),
            new OrganizationId(organization ?? Organization),
            null,
            new PlayerDetails(firstName, lastName, email, phone, documentId, birthDate, gender, category, club),
            isActive,
            RegistrationsTestHarness.Now.AddDays(-30),
            RegistrationsTestHarness.Now.AddDays(-30));
        R.Players.Seed(player);
        return player;
    }

    /// <summary>Sends a code to <paramref name="email"/> through the real handler and returns it as the email carried it.</summary>
    public async Task<string> CodeAsync(string email = "contact@club.test", string slug = Slug)
    {
        var sent = await SendCode.Handle(new SendPublicRegistrationCodeCommand(slug, new PublicRegistrationCodeBody(email)), CancellationToken.None);
        Assert.True(sent.IsSuccess);
        return Templates.Composed[^1].Variables!["code"];
    }

    /// <summary>Snapshot of every store the public flow may write, to prove a refused request wrote nothing.</summary>
    public string StoreSnapshot() => string.Join('|',
        string.Join(',', R.Players.Writes),
        string.Join(',', R.Registrations.Writes),
        string.Join(',', R.Registrations.RejectedWrites),
        string.Join(',', Cache.Entries.OrderBy(e => e.Key).Select(e => $"{e.Key}={e.Value}")),
        Email.Sent.Count);

    private sealed class HandlerSender(PublicRegistrationsTestHarness h) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                GetPublicRegistrationInfoQuery query => await h.Info.Handle(query, cancellationToken),
                SendPublicRegistrationCodeCommand command => await h.SendCode.Handle(command, cancellationToken),
                SubmitPublicRegistrationCommand command => await h.Submit.Handle(command, cancellationToken),
                GetPublicConfirmedPairsQuery query => await h.ConfirmedPairs.Handle(query, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name),
            };
            return (TResponse)result;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

/// <summary>A cache that keeps every entry (the handlers own expiry) and records the time to live of each write.</summary>
internal sealed class RecordingCacheStore : ICacheStore
{
    public Dictionary<string, object?> Entries { get; } = [];

    public List<(string Key, TimeSpan? Ttl)> Sets { get; } = [];

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Entries.TryGetValue(key, out var value) && value is T typed ? typed : default);

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        Entries[key] = value;
        Sets.Add((key, ttl));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Entries.Remove(key);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingEmailService : IEmailService
{
    public List<EmailMessage> Sent { get; } = [];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>Composes the fallback message (no stored template) and records the template key and variables.</summary>
internal sealed class RecordingTemplateComposer : IEmailTemplateComposer
{
    public List<(string To, string TemplateKey, IReadOnlyDictionary<string, string>? Variables)> Composed { get; } = [];

    public Task<EmailMessage> ComposeAsync(
        string to,
        string templateKey,
        string fallbackSubject,
        string fallbackHtmlBody,
        IReadOnlyDictionary<string, string>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Composed.Add((to, templateKey, variables));
        return Task.FromResult(new EmailMessage(to, fallbackSubject, fallbackHtmlBody));
    }
}
