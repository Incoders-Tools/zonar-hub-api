using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Domain.Draws.Results;
using ZonarHub.Infrastructure.Persistence.Supabase.Draws;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

/// <summary>
/// PostgREST store of tournament draws and match results (migration <c>20261031100000_tournament_draws</c>). Each
/// operation is one HTTP call, so each is atomic in the database; see <see cref="ITournamentDrawRepository"/> for the
/// races left to callers. Enum columns use <see cref="DrawWireNames"/>, jsonb columns <see cref="TournamentDrawJson"/>,
/// and timestamps are written and read as UTC. Store failures throw <see cref="HttpRequestException"/>; stored values
/// outside the contract throw <see cref="JsonException"/>.
/// </summary>
internal sealed class TournamentDrawRepository : ITournamentDrawRepository
{
    private const string DrawsPath = "/rest/v1/tournament_draws";
    private const string ResultsPath = "/rest/v1/tournament_draw_match_results";
    private const string UpsertPrefer = "resolution=merge-duplicates,return=representation";
    private const string ReturnPrefer = "return=representation";
    private const string WithResults = "select=*,results:tournament_draw_match_results(*)";

    /// <summary>The published read never selects the submitted request, the owner ids or court ids.</summary>
    private const string PublicSelect =
        "select=tournament_id,options,structure,published_at_utc,updated_at_utc,results:tournament_draw_match_results(" +
        "match_id,status,sets,winner_side,court_name,scheduled_at_utc,started_at_utc,finished_at_utc,updated_at_utc)";

    private const string ResultsOrder = "results.order=match_id.asc";

    private readonly HttpClient _http;
    private readonly IClock _clock;

    public TournamentDrawRepository(IHttpClientFactory factory, IClock clock)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _clock = clock;
    }

    public async Task<TournamentDrawSnapshot?> GetByTournamentIdAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default)
    {
        var url = $"{DrawsPath}?{WithResults}&tournament_id=eq.{tournamentId}&{ResultsOrder}&limit=1";
        var rows = await SendAsync<DrawRow>(new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        return rows.Count == 0
            ? null
            : new TournamentDrawSnapshot(ToRecord(rows[0]), (rows[0].Results ?? []).Select(ToResult).ToList());
    }

    public async Task<TournamentDrawRecord> SaveDraftAsync(TournamentDrawDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var structure = draft.Structure;
        // id and created_at_utc are left out so a replacement keeps them (the conflict update only sets sent columns).
        var row = new DrawWriteRow(
            draft.TournamentId,
            draft.OrganizationId,
            TournamentDrawStatus.Draft.ToWireName(),
            structure.Format.ToWireName(),
            structure.ParticipationFormat.ToWireName(),
            structure.RandomizationMode.ToWireName(),
            structure.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TournamentDrawJson.WriteOptions(draft.Request.Options, draft.Scoring),
            TournamentDrawJson.WriteRequest(draft.Request, draft.Scoring),
            TournamentDrawJson.WriteDraw(structure),
            null,
            _clock.UtcNow);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{DrawsPath}?on_conflict=tournament_id&select=*")
        {
            Content = JsonContent.Create(row),
        };
        request.Headers.Add("Prefer", UpsertPrefer);

        var rows = await SendAsync<DrawRow>(request, cancellationToken);
        return ToRecord(rows[0]);
    }

    public async Task<TournamentDrawRecord?> SetStatusAsync(
        Guid drawId,
        TournamentDrawStatus status,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var patch = new StatusPatch(
            status.ToWireName(),
            status == TournamentDrawStatus.Published ? now : null,
            now);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"{DrawsPath}?id=eq.{drawId}&select=*")
        {
            Content = JsonContent.Create(patch),
        };
        request.Headers.Add("Prefer", ReturnPrefer);

        var rows = await SendAsync<DrawRow>(request, cancellationToken);
        return rows.Count == 0 ? null : ToRecord(rows[0]);
    }

    public async Task<bool> DeleteDraftAsync(Guid drawId, CancellationToken cancellationToken = default)
    {
        var url = $"{DrawsPath}?id=eq.{drawId}&status=eq.{TournamentDrawStatus.Draft.ToWireName()}&select=id";
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Add("Prefer", ReturnPrefer);

        var rows = await SendAsync<JsonElement>(request, cancellationToken);
        return rows.Count > 0;
    }

    public async Task<TournamentDrawMatchResult> UpsertResultAsync(
        Guid drawId,
        MatchResult result,
        Guid? courtId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var row = new ResultWriteRow(
            drawId,
            result.MatchId,
            result.Status.ToWireName(),
            TournamentDrawJson.WriteSets(result.Sets),
            result.WinnerSide?.ToWireName(),
            courtId,
            result.CourtName,
            Utc(result.ScheduledAtUtc),
            Utc(result.StartedAtUtc),
            Utc(result.FinishedAtUtc),
            _clock.UtcNow);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{ResultsPath}?on_conflict=draw_id,match_id&select=*")
        {
            Content = JsonContent.Create(row),
        };
        request.Headers.Add("Prefer", UpsertPrefer);

        var rows = await SendAsync<ResultRow>(request, cancellationToken);
        return ToResult(rows[0]);
    }

    public async Task<IReadOnlyList<TournamentDrawMatchResult>> ListResultsAsync(
        Guid drawId,
        CancellationToken cancellationToken = default)
    {
        var url = $"{ResultsPath}?select=*&draw_id=eq.{drawId}&order=match_id.asc";
        var rows = await SendAsync<ResultRow>(new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        return rows.Select(ToResult).ToList();
    }

    public async Task<PublishedTournamentDraw?> GetPublishedByTournamentIdAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default)
    {
        var url = $"{DrawsPath}?{PublicSelect}&tournament_id=eq.{tournamentId}" +
            $"&status=eq.{TournamentDrawStatus.Published.ToWireName()}&{ResultsOrder}&limit=1";
        var rows = await SendAsync<PublishedDrawRow>(new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        if (rows.Count == 0)
            return null;

        var row = rows[0];
        var (options, scoring) = TournamentDrawJson.ReadOptions(row.Options);
        return new PublishedTournamentDraw(
            row.TournamentId,
            TournamentDrawJson.ReadDraw(row.Structure),
            options,
            scoring,
            row.PublishedAtUtc.UtcDateTime,
            row.UpdatedAtUtc.UtcDateTime,
            (row.Results ?? []).Select(ToResult).ToList());
    }

    private async Task<IReadOnlyList<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var rows = await JsonSerializer.DeserializeAsync<List<T>>(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            return rows ?? [];
        }
    }

    private static TournamentDrawRecord ToRecord(DrawRow row)
    {
        var (options, scoring) = TournamentDrawJson.ReadOptions(row.Options);
        return new TournamentDrawRecord(
            row.Id,
            row.TournamentId,
            row.OrganizationId,
            Parse<TournamentDrawStatus>(row.Status, "status", DrawWireNames.TryParse),
            TournamentDrawJson.ReadDraw(row.Structure),
            TournamentDrawJson.ReadRequest(row.Request).Request,
            options,
            scoring,
            row.PublishedAtUtc?.UtcDateTime,
            row.CreatedAtUtc.UtcDateTime,
            row.UpdatedAtUtc.UtcDateTime);
    }

    private static TournamentDrawMatchResult ToResult(ResultRow row) => new(
        new MatchResult(
            row.MatchId,
            Parse<MatchStatus>(row.Status, "status", DrawWireNames.TryParse),
            TournamentDrawJson.ReadSets(row.Sets),
            row.WinnerSide is null ? null : Parse<MatchSide>(row.WinnerSide, "winner_side", DrawWireNames.TryParse),
            row.CourtName,
            row.ScheduledAtUtc?.UtcDateTime,
            row.StartedAtUtc?.UtcDateTime,
            row.FinishedAtUtc?.UtcDateTime),
        row.CourtId,
        row.UpdatedAtUtc.UtcDateTime);

    private delegate bool Parser<T>(string? name, out T value);

    private static T Parse<T>(string value, string column, Parser<T> parse) =>
        parse(value, out var parsed) ? parsed : throw new JsonException($"Unknown stored {column} '{value}'.");

    /// <summary>Local times are converted; unspecified times are already UTC by the <see cref="MatchResult"/> contract.</summary>
    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        var other => DateTime.SpecifyKind(other.Value, DateTimeKind.Utc),
    };

    private sealed record DrawWriteRow(
        [property: JsonPropertyName("tournament_id")] Guid TournamentId,
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("participation_format")] string ParticipationFormat,
        [property: JsonPropertyName("randomization_mode")] string RandomizationMode,
        [property: JsonPropertyName("seed")] string Seed,
        [property: JsonPropertyName("options")] JsonElement Options,
        [property: JsonPropertyName("request")] JsonElement Request,
        [property: JsonPropertyName("structure")] JsonElement Structure,
        [property: JsonPropertyName("published_at_utc")] DateTime? PublishedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);

    private sealed record StatusPatch(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("published_at_utc")] DateTime? PublishedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);

    private sealed record DrawRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("tournament_id")] Guid TournamentId,
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("options")] JsonElement Options,
        [property: JsonPropertyName("request")] JsonElement Request,
        [property: JsonPropertyName("structure")] JsonElement Structure,
        [property: JsonPropertyName("published_at_utc")] DateTimeOffset? PublishedAtUtc,
        [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
        [property: JsonPropertyName("results")] List<ResultRow>? Results = null);

    private sealed record PublishedDrawRow(
        [property: JsonPropertyName("tournament_id")] Guid TournamentId,
        [property: JsonPropertyName("options")] JsonElement Options,
        [property: JsonPropertyName("structure")] JsonElement Structure,
        [property: JsonPropertyName("published_at_utc")] DateTimeOffset PublishedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
        [property: JsonPropertyName("results")] List<ResultRow>? Results = null);

    private sealed record ResultWriteRow(
        [property: JsonPropertyName("draw_id")] Guid DrawId,
        [property: JsonPropertyName("match_id")] string MatchId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("sets")] JsonElement Sets,
        [property: JsonPropertyName("winner_side")] string? WinnerSide,
        [property: JsonPropertyName("court_id")] Guid? CourtId,
        [property: JsonPropertyName("court_name")] string? CourtName,
        [property: JsonPropertyName("scheduled_at_utc")] DateTime? ScheduledAtUtc,
        [property: JsonPropertyName("started_at_utc")] DateTime? StartedAtUtc,
        [property: JsonPropertyName("finished_at_utc")] DateTime? FinishedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);

    private sealed record ResultRow(
        [property: JsonPropertyName("match_id")] string MatchId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("sets")] JsonElement Sets,
        [property: JsonPropertyName("winner_side")] string? WinnerSide,
        [property: JsonPropertyName("court_name")] string? CourtName,
        [property: JsonPropertyName("scheduled_at_utc")] DateTimeOffset? ScheduledAtUtc,
        [property: JsonPropertyName("started_at_utc")] DateTimeOffset? StartedAtUtc,
        [property: JsonPropertyName("finished_at_utc")] DateTimeOffset? FinishedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
        [property: JsonPropertyName("court_id")] Guid? CourtId = null);
}
