using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Sports;
using ZonarHub.Domain.Tournaments;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class TournamentRepository : ITournamentRepository
{
    private const string RestPath = "/rest/v1/tournaments";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public TournamentRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<Tournament?> GetByIdAsync(TournamentId id, CancellationToken cancellationToken = default)
    {
        var rows = await _http.GetFromJsonAsync<List<TournamentRow>>(
            $"{RestPath}?select=*&id=eq.{id.Value}&limit=1",
            cancellationToken);

        var row = rows?.FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public Task AddAsync(Tournament tournament, CancellationToken cancellationToken = default)
    {
        var row = ToRow(tournament);
        _ops.Enqueue((http, ct) => ExecuteAddAsync(http, row, ct));
        return Task.CompletedTask;
    }

    private static Tournament ToDomain(TournamentRow row) =>
        Tournament.Reconstitute(
            new TournamentId(row.Id),
            new OrganizationId(row.OrganizationId),
            row.ComplexId is { } complexId ? new ComplexId(complexId) : null,
            new SportId(row.SportId),
            row.Name,
            row.StartDate,
            row.EndDate,
            ParseStatus(row.Status),
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static TournamentRow ToRow(Tournament tournament) =>
        new(
            tournament.Id.Value,
            tournament.OrganizationId.Value,
            tournament.ComplexId?.Value,
            tournament.SportId.Value,
            tournament.Name,
            tournament.StartDate,
            tournament.EndDate,
            ToStatusStorage(tournament.Status),
            tournament.IsActive,
            tournament.CreatedAtUtc,
            tournament.UpdatedAtUtc);

    private static TournamentStatus ParseStatus(string value) => value.ToLowerInvariant() switch
    {
        "active" => TournamentStatus.Active,
        "finished" => TournamentStatus.Finished,
        "cancelled" => TournamentStatus.Cancelled,
        _ => TournamentStatus.Upcoming,
    };

    private static string ToStatusStorage(TournamentStatus status) => status switch
    {
        TournamentStatus.Active => "active",
        TournamentStatus.Finished => "finished",
        TournamentStatus.Cancelled => "cancelled",
        _ => "upcoming",
    };

    private static async Task ExecuteAddAsync(HttpClient http, TournamentRow row, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RestPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record TournamentRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("complex_id")] Guid? ComplexId,
        [property: JsonPropertyName("sport_id")] Guid SportId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("start_date")] DateOnly StartDate,
        [property: JsonPropertyName("end_date")] DateOnly EndDate,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);
}
