using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Domain.Draws.Results;
using ZonarHub.Infrastructure.Persistence.Supabase.Draws;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

/// <summary>
/// PostgREST store of tournament draws and match results (migrations <c>20261031100000_tournament_draws</c> and
/// <c>20261101100000_tournament_draw_revisions</c>). Reads are table queries; every write is one call to a SQL function
/// that checks the expected revision and applies the change in one transaction (D12). A function refuses a write with
/// SQLSTATE <c>PT409</c> (HTTP 409) and a <c>draws.*</c> message, mapped to <see cref="TournamentDrawConcurrencyException"/>.
/// Enum columns use <see cref="DrawWireNames"/>, jsonb columns <see cref="TournamentDrawJson"/>, and timestamps are
/// written and read as UTC. Other store failures throw <see cref="HttpRequestException"/>; stored values outside the
/// contract throw <see cref="JsonException"/>.
/// </summary>
internal sealed class TournamentDrawRepository : ITournamentDrawRepository
{
    private const string DrawsPath = "/rest/v1/tournament_draws";
    private const string SaveDraftRpc = "/rest/v1/rpc/save_tournament_draw_draft";
    private const string SetStatusRpc = "/rest/v1/rpc/set_tournament_draw_status";
    private const string DeleteDraftRpc = "/rest/v1/rpc/delete_tournament_draw_draft";
    private const string UpsertResultRpc = "/rest/v1/rpc/upsert_tournament_draw_match_result";
    private const string WithResults = "select=*,results:tournament_draw_match_results(*)";

    /// <summary>SQLSTATE the draw functions raise to refuse a write; PostgREST answers it with HTTP 409.</summary>
    private const string RefusedWriteCode = "PT409";

    private static readonly IReadOnlyDictionary<string, TournamentDrawWriteConflict> RefusedWrites =
        new Dictionary<string, TournamentDrawWriteConflict>(StringComparer.Ordinal)
        {
            ["draws.concurrent_modification"] = TournamentDrawWriteConflict.ConcurrentModification,
            ["draws.results_exist"] = TournamentDrawWriteConflict.ResultsExist,
            ["draws.not_draft"] = TournamentDrawWriteConflict.NotDraft,
        };

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

    public async Task<TournamentDrawRecord> SaveDraftAsync(
        TournamentDrawDraft draft,
        TournamentDrawVersion? expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var structure = draft.Structure;
        var parameters = new SaveDraftParameters(
            draft.TournamentId,
            draft.OrganizationId,
            expected?.DrawId,
            expected?.Revision,
            structure.Format.ToWireName(),
            structure.ParticipationFormat.ToWireName(),
            structure.RandomizationMode.ToWireName(),
            structure.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TournamentDrawJson.WriteOptions(draft.Request.Options, draft.Scoring),
            TournamentDrawJson.WriteRequest(draft.Request, draft.Scoring),
            TournamentDrawJson.WriteDraw(structure),
            _clock.UtcNow);

        return ToRecord(await CallAsync<DrawRow>(SaveDraftRpc, parameters, cancellationToken));
    }

    public async Task<TournamentDrawRecord> SetStatusAsync(
        TournamentDrawVersion expected,
        TournamentDrawStatus status,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);

        var parameters = new StatusParameters(expected.DrawId, expected.Revision, status.ToWireName(), _clock.UtcNow);
        return ToRecord(await CallAsync<DrawRow>(SetStatusRpc, parameters, cancellationToken));
    }

    public async Task DeleteDraftAsync(TournamentDrawVersion expected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);

        // The function answers the deleted id and the revision it reached; nothing is left to map.
        await CallAsync<JsonElement>(DeleteDraftRpc, new DeleteParameters(expected.DrawId, expected.Revision), cancellationToken);
    }

    public async Task<RecordedTournamentDrawResult> UpsertResultAsync(
        TournamentDrawVersion expected,
        MatchResult result,
        Guid? courtId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(result);

        var parameters = new ResultParameters(
            expected.DrawId,
            expected.Revision,
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

        var recorded = await CallAsync<RecordedResultRow>(UpsertResultRpc, parameters, cancellationToken);
        return new RecordedTournamentDrawResult(ToResult(recorded.Result), recorded.Revision);
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

    /// <summary>
    /// Calls a draw function (one POST with the named parameters) and reads its jsonb answer. A refused write becomes
    /// <see cref="TournamentDrawConcurrencyException"/>; any other failure keeps the <see cref="HttpRequestException"/> path.
    /// </summary>
    private async Task<T> CallAsync<T>(string path, object parameters, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(path, parameters, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict
            && await ReadRefusedWriteAsync(response, cancellationToken) is { } conflict)
            throw new TournamentDrawConcurrencyException(conflict);

        response.EnsureSuccessStatusCode();
        var value = await JsonSerializer.DeserializeAsync<T>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return value ?? throw new JsonException($"The function {path} returned no result.");
    }

    /// <summary>
    /// The conflict of a PostgREST error body raised by a draw function
    /// (<c>{"code":"PT409","message":"draws.concurrent_modification", ...}</c>); null for any other 409.
    /// </summary>
    private static async Task<TournamentDrawWriteConflict?> ReadRefusedWriteAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        PostgrestError? error;
        try
        {
            error = JsonSerializer.Deserialize<PostgrestError>(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (JsonException)
        {
            return null;
        }

        return error?.Code == RefusedWriteCode && error.Message is { } message && RefusedWrites.TryGetValue(message, out var conflict)
            ? conflict
            : null;
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
            row.UpdatedAtUtc.UtcDateTime,
            row.Revision);
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

    private sealed record SaveDraftParameters(
        [property: JsonPropertyName("p_tournament_id")] Guid TournamentId,
        [property: JsonPropertyName("p_organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("p_expected_draw_id")] Guid? ExpectedDrawId,
        [property: JsonPropertyName("p_expected_revision")] int? ExpectedRevision,
        [property: JsonPropertyName("p_format")] string Format,
        [property: JsonPropertyName("p_participation_format")] string ParticipationFormat,
        [property: JsonPropertyName("p_randomization_mode")] string RandomizationMode,
        [property: JsonPropertyName("p_seed")] string Seed,
        [property: JsonPropertyName("p_options")] JsonElement Options,
        [property: JsonPropertyName("p_request")] JsonElement Request,
        [property: JsonPropertyName("p_structure")] JsonElement Structure,
        [property: JsonPropertyName("p_now_utc")] DateTime NowUtc);

    private sealed record StatusParameters(
        [property: JsonPropertyName("p_draw_id")] Guid DrawId,
        [property: JsonPropertyName("p_expected_revision")] int ExpectedRevision,
        [property: JsonPropertyName("p_status")] string Status,
        [property: JsonPropertyName("p_now_utc")] DateTime NowUtc);

    private sealed record DeleteParameters(
        [property: JsonPropertyName("p_draw_id")] Guid DrawId,
        [property: JsonPropertyName("p_expected_revision")] int ExpectedRevision);

    private sealed record ResultParameters(
        [property: JsonPropertyName("p_draw_id")] Guid DrawId,
        [property: JsonPropertyName("p_expected_revision")] int ExpectedRevision,
        [property: JsonPropertyName("p_match_id")] string MatchId,
        [property: JsonPropertyName("p_status")] string Status,
        [property: JsonPropertyName("p_sets")] JsonElement Sets,
        [property: JsonPropertyName("p_winner_side")] string? WinnerSide,
        [property: JsonPropertyName("p_court_id")] Guid? CourtId,
        [property: JsonPropertyName("p_court_name")] string? CourtName,
        [property: JsonPropertyName("p_scheduled_at_utc")] DateTime? ScheduledAtUtc,
        [property: JsonPropertyName("p_started_at_utc")] DateTime? StartedAtUtc,
        [property: JsonPropertyName("p_finished_at_utc")] DateTime? FinishedAtUtc,
        [property: JsonPropertyName("p_now_utc")] DateTime NowUtc);

    private sealed record RecordedResultRow(
        [property: JsonPropertyName("revision")] int Revision,
        [property: JsonPropertyName("result")] ResultRow Result);

    private sealed record PostgrestError(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("message")] string? Message);

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
        [property: JsonPropertyName("revision")] int Revision,
        [property: JsonPropertyName("results")] List<ResultRow>? Results = null);

    private sealed record PublishedDrawRow(
        [property: JsonPropertyName("tournament_id")] Guid TournamentId,
        [property: JsonPropertyName("options")] JsonElement Options,
        [property: JsonPropertyName("structure")] JsonElement Structure,
        [property: JsonPropertyName("published_at_utc")] DateTimeOffset PublishedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
        [property: JsonPropertyName("results")] List<ResultRow>? Results = null);

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
