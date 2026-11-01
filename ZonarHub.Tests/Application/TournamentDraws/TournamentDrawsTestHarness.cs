using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Application.Features.TournamentDraws.Delete;
using ZonarHub.Application.Features.TournamentDraws.Get;
using ZonarHub.Application.Features.TournamentDraws.Preview;
using ZonarHub.Application.Features.TournamentDraws.Results;
using ZonarHub.Application.Features.TournamentDraws.Save;
using ZonarHub.Application.Features.TournamentDraws.Status;
using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Draws.Results;
using ZonarHub.Domain.Users;
using ZonarHub.Tests.Application.AdminTournaments;

namespace ZonarHub.Tests.Application.TournamentDraws;

/// <summary>
/// Wires the real draw handlers to the in-memory user/organization stores, the recording tournament repository and an
/// in-memory draw repository.
/// </summary>
internal sealed class TournamentDrawsTestHarness
{
    public static readonly DateTime Now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    public TournamentDrawsTestHarness()
    {
        Tournaments = new AdminTournamentsListTestHarness(Now);
        Draws = new InMemoryTournamentDrawRepository(Now);
        var users = Tournaments.Users;
        Preview = new PreviewTournamentDrawHandler(users.CurrentUser, users.Users, users.Organizations, Tournaments.Tournaments);
        Save = new SaveTournamentDrawHandler(users.CurrentUser, users.Users, users.Organizations, Tournaments.Tournaments, Draws);
        Get = new GetTournamentDrawHandler(users.CurrentUser, users.Users, users.Organizations, Tournaments.Tournaments, Draws);
        Status = new SetTournamentDrawStatusHandler(users.CurrentUser, users.Users, users.Organizations, Tournaments.Tournaments, Draws);
        Delete = new DeleteTournamentDrawHandler(users.CurrentUser, users.Users, users.Organizations, Tournaments.Tournaments, Draws);
        Record = new RecordMatchResultHandler(users.CurrentUser, users.Users, users.Organizations, Tournaments.Tournaments, Draws);
        Sender = new HandlerSender(this);
    }

    public AdminTournamentsListTestHarness Tournaments { get; }
    public InMemoryTournamentDrawRepository Draws { get; }
    public PreviewTournamentDrawHandler Preview { get; }
    public SaveTournamentDrawHandler Save { get; }
    public GetTournamentDrawHandler Get { get; }
    public SetTournamentDrawStatusHandler Status { get; }
    public DeleteTournamentDrawHandler Delete { get; }
    public RecordMatchResultHandler Record { get; }
    public ISender Sender { get; }

    /// <summary>A tournament of the caller's own tenant (admin caller).</summary>
    public async Task<TournamentAdminDto> OwnTournamentAsync()
    {
        var tenant = Guid.NewGuid();
        var org = await Tournaments.Users.SeedOrganizationAsync(tenant, "Org A");
        var tournament = Tournaments.Tournaments.Seed(org.Id.Value, "Open A");
        await AdminTournamentsItemAccessTests.AuthenticateAsync(Tournaments, UserRole.Admin, tenant);
        return tournament;
    }

    /// <summary>A tournament of another tenant, with a stored draft so a refusal cannot be a 404.</summary>
    public async Task<TournamentAdminDto> ForeignTournamentWithDrawAsync()
    {
        var foreignOrg = await Tournaments.Users.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var tournament = Tournaments.Tournaments.Seed(foreignOrg.Id.Value, "Foreign Open");
        await AdminTournamentsItemAccessTests.AuthenticateAsync(Tournaments, UserRole.Admin, Guid.NewGuid());
        Draws.SeedDraft(tournament, TournamentDrawBodies.Knockout(4));
        return tournament;
    }

    /// <summary>Routes each draw request to the matching real handler.</summary>
    private sealed class HandlerSender(TournamentDrawsTestHarness h) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                PreviewTournamentDrawCommand command => await h.Preview.Handle(command, cancellationToken),
                SaveTournamentDrawCommand command => await h.Save.Handle(command, cancellationToken),
                GetTournamentDrawQuery query => await h.Get.Handle(query, cancellationToken),
                SetTournamentDrawStatusCommand command => await h.Status.Handle(command, cancellationToken),
                DeleteTournamentDrawCommand command => await h.Delete.Handle(command, cancellationToken),
                RecordMatchResultCommand command => await h.Record.Handle(command, cancellationToken),
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

/// <summary>Request bodies as the frontend client sends them.</summary>
internal static class TournamentDrawBodies
{
    public static TournamentDrawRequestBody Knockout(int participants, string? seed = "7", string? separationKey = null) => new(
        "singleElimination",
        "pair",
        new DrawRandomizationBody("seeded", seed),
        new DrawOptionsBody(Knockout: new KnockoutOptionsBody(false), Scoring: new ScoringOptionsBody(true)),
        Participants(participants, separationKey));

    public static TournamentDrawRequestBody RoundRobin(int participants) => new(
        "roundRobin",
        "pair",
        new DrawRandomizationBody("random", "11"),
        new DrawOptionsBody(RoundRobin: new RoundRobinOptionsBody(1)),
        Participants(participants));

    public static TournamentDrawRequestBody Groups(int participants) => new(
        "groupsThenKnockout",
        "pair",
        new DrawRandomizationBody("seeded", "5"),
        new DrawOptionsBody(
            new KnockoutOptionsBody(false),
            new RoundRobinOptionsBody(1),
            new GroupStageOptionsBody(GroupCount: 2, QualifiersPerGroup: 2)),
        Participants(participants));

    public static IReadOnlyList<DrawParticipantBody?> Participants(int count, string? separationKey = null) =>
        Enumerable.Range(1, count)
            .Select(i => (DrawParticipantBody?)new DrawParticipantBody(
                $"p{i}", $"Pair {i}", [$"Ana {i}", $"Bea {i}"], i == 1 ? 1 : null, separationKey))
            .ToList();

    public static MatchResultBody Completed(string winnerSide = "home") => new(
        "completed",
        [new SetScoreBody(winnerSide == "home" ? 6 : 4, winnerSide == "home" ? 4 : 6, null),
         new SetScoreBody(winnerSide == "home" ? 7 : 6, winnerSide == "home" ? 6 : 7, new TiebreakScoreBody(winnerSide == "home" ? 7 : 5, winnerSide == "home" ? 5 : 7))],
        winnerSide,
        null,
        "Court 1",
        new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(-3)),
        null,
        null);
}

/// <summary>
/// Map-backed <see cref="ITournamentDrawRepository"/> that records every write. <see cref="SimulateConcurrentDelete"/>
/// makes status changes and deletes behave as if the draw vanished between the caller's read and the write;
/// <see cref="ResultRecordedBeforeRecheck"/> is stored when the caller re-lists results (a result recorded meanwhile).
/// </summary>
internal sealed class InMemoryTournamentDrawRepository(DateTime nowUtc) : ITournamentDrawRepository
{
    private readonly Dictionary<Guid, TournamentDrawRecord> _draws = [];
    private readonly Dictionary<Guid, Dictionary<string, TournamentDrawMatchResult>> _results = [];

    public List<string> Writes { get; } = [];

    public List<TournamentDrawDraft> SavedDrafts { get; } = [];

    public List<(Guid DrawId, MatchResult Result, Guid? CourtId)> UpsertedResults { get; } = [];

    public bool SimulateConcurrentDelete { get; set; }

    public MatchResult? ResultRecordedBeforeRecheck { get; set; }

    /// <summary>Tournament ids asked for by <see cref="GetPublishedByTournamentIdAsync"/>.</summary>
    public List<Guid> PublishedLookups { get; } = [];

    public TournamentDrawRecord SeedDraft(TournamentAdminDto tournament, TournamentDrawRequestBody body) =>
        SeedDraft(tournament.Id, tournament.OrganizationId, body);

    public TournamentDrawRecord SeedDraft(Guid tournamentId, Guid organizationId, TournamentDrawRequestBody body)
    {
        var submission = TournamentDrawRequestMapper.ToSubmission(body);
        var draw = DrawGenerator.Generate(submission.Value.Seeded);
        return Store(new TournamentDrawDraft(
            tournamentId, organizationId, submission.Value.Request, submission.Value.Scoring, draw.Value));
    }

    public TournamentDrawRecord? Find(Guid tournamentId) =>
        _draws.Values.SingleOrDefault(draw => draw.TournamentId == tournamentId);

    public void SeedResult(Guid tournamentId, MatchResult result) =>
        Results(Find(tournamentId)!.Id)[result.MatchId] = new TournamentDrawMatchResult(result, null, nowUtc);

    public void SeedStatus(Guid tournamentId, TournamentDrawStatus status)
    {
        var record = Find(tournamentId)!;
        _draws[record.Id] = record with
        {
            Status = status,
            PublishedAtUtc = status == TournamentDrawStatus.Published ? nowUtc : null,
        };
    }

    public Task<TournamentDrawSnapshot?> GetByTournamentIdAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var record = Find(tournamentId);
        return Task.FromResult(record is null ? null : new TournamentDrawSnapshot(record, Ordered(record.Id)));
    }

    public Task<TournamentDrawRecord> SaveDraftAsync(TournamentDrawDraft draft, CancellationToken cancellationToken = default)
    {
        Writes.Add("save");
        SavedDrafts.Add(draft);
        return Task.FromResult(Store(draft));
    }

    public Task<TournamentDrawRecord?> SetStatusAsync(Guid drawId, TournamentDrawStatus status, CancellationToken cancellationToken = default)
    {
        Writes.Add($"status:{status}");
        if (SimulateConcurrentDelete || !_draws.TryGetValue(drawId, out var record))
            return Task.FromResult<TournamentDrawRecord?>(null);

        var updated = record with
        {
            Status = status,
            PublishedAtUtc = status == TournamentDrawStatus.Published ? nowUtc : null,
            UpdatedAtUtc = nowUtc,
        };
        _draws[drawId] = updated;
        return Task.FromResult<TournamentDrawRecord?>(updated);
    }

    public Task<bool> DeleteDraftAsync(Guid drawId, CancellationToken cancellationToken = default)
    {
        Writes.Add("delete");
        if (SimulateConcurrentDelete || !_draws.TryGetValue(drawId, out var record) || record.Status != TournamentDrawStatus.Draft)
            return Task.FromResult(false);

        _draws.Remove(drawId);
        _results.Remove(drawId);
        return Task.FromResult(true);
    }

    public Task<TournamentDrawMatchResult> UpsertResultAsync(Guid drawId, MatchResult result, Guid? courtId, CancellationToken cancellationToken = default)
    {
        Writes.Add($"result:{result.MatchId}");
        UpsertedResults.Add((drawId, result, courtId));
        var stored = new TournamentDrawMatchResult(result, courtId, nowUtc);
        Results(drawId)[result.MatchId] = stored;
        return Task.FromResult(stored);
    }

    public Task<IReadOnlyList<TournamentDrawMatchResult>> ListResultsAsync(Guid drawId, CancellationToken cancellationToken = default)
    {
        if (ResultRecordedBeforeRecheck is { } recorded)
            Results(drawId)[recorded.MatchId] = new TournamentDrawMatchResult(recorded, null, nowUtc);

        return Task.FromResult(Ordered(drawId));
    }

    /// <summary>Like the store: only a published draw, with its options, scoring and every result (court ids not read).</summary>
    public Task<PublishedTournamentDraw?> GetPublishedByTournamentIdAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        PublishedLookups.Add(tournamentId);
        var record = Find(tournamentId);
        if (record is null || record.Status != TournamentDrawStatus.Published)
            return Task.FromResult<PublishedTournamentDraw?>(null);

        var results = Ordered(record.Id).Select(result => result with { CourtId = null }).ToList();
        return Task.FromResult<PublishedTournamentDraw?>(new PublishedTournamentDraw(
            record.TournamentId,
            record.Structure,
            record.Options,
            record.Scoring,
            record.PublishedAtUtc!.Value,
            record.UpdatedAtUtc,
            results));
    }

    private TournamentDrawRecord Store(TournamentDrawDraft draft)
    {
        var existing = Find(draft.TournamentId);
        var record = new TournamentDrawRecord(
            existing?.Id ?? Guid.NewGuid(),
            draft.TournamentId,
            draft.OrganizationId,
            TournamentDrawStatus.Draft,
            draft.Structure,
            draft.Request,
            draft.Request.Options ?? new DrawOptions(),
            draft.Scoring,
            null,
            existing?.CreatedAtUtc ?? nowUtc,
            nowUtc);
        _draws[record.Id] = record;
        return record;
    }

    private Dictionary<string, TournamentDrawMatchResult> Results(Guid drawId)
    {
        if (!_results.TryGetValue(drawId, out var results))
        {
            results = new Dictionary<string, TournamentDrawMatchResult>(StringComparer.Ordinal);
            _results[drawId] = results;
        }

        return results;
    }

    private IReadOnlyList<TournamentDrawMatchResult> Ordered(Guid drawId) =>
        Results(drawId).Values.OrderBy(result => result.Result.MatchId, StringComparer.Ordinal).ToList();
}
