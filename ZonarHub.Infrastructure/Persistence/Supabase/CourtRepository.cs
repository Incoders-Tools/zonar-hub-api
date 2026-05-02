using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Courts;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class CourtRepository : ICourtRepository
{
    private const string RestPath = "/rest/v1/courts";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public CourtRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<IReadOnlyList<Court>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _http.GetFromJsonAsync<List<CourtRow>>(
            $"{RestPath}?select=*&order=name.asc",
            cancellationToken) ?? [];

        return rows.Select(ToDomain).ToList();
    }

    public async Task<Court?> GetByIdAsync(CourtId id, CancellationToken cancellationToken = default)
    {
        var rows = await _http.GetFromJsonAsync<List<CourtRow>>(
            $"{RestPath}?select=*&id=eq.{id.Value}",
            cancellationToken) ?? [];

        return rows.FirstOrDefault() is { } row ? ToDomain(row) : null;
    }

    public async Task<IReadOnlyList<Court>> ListByComplexIdAsync(
        ComplexId complexId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _http.GetFromJsonAsync<List<CourtRow>>(
            $"{RestPath}?select=*&complex_id=eq.{complexId.Value}&order=name.asc",
            cancellationToken) ?? [];

        return rows.Select(ToDomain).ToList();
    }

    public Task AddAsync(Court court, CancellationToken cancellationToken = default)
    {
        var row = ToRow(court);
        _ops.Enqueue((http, ct) => ExecuteAddAsync(http, row, ct));
        return Task.CompletedTask;
    }

    public void Update(Court court)
    {
        var row = ToRow(court);
        _ops.Enqueue((http, ct) => ExecuteUpdateAsync(http, row.Id, row, ct));
    }

    public void Remove(Court court)
    {
        _ops.Enqueue((http, ct) => ExecuteDeleteAsync(http, court.Id.Value, ct));
    }

    private static Court ToDomain(CourtRow row) =>
        Court.Reconstitute(
            new CourtId(row.Id),
            new ComplexId(row.ComplexId),
            row.Name,
            row.IsActive,
            row.CreatedAtUtc);

    private static CourtRow ToRow(Court court) =>
        new(
            court.Id.Value,
            court.ComplexId.Value,
            court.Name,
            court.IsActive,
            court.CreatedAtUtc);

    private static async Task ExecuteAddAsync(HttpClient http, CourtRow row, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RestPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task ExecuteUpdateAsync(HttpClient http, Guid id, CourtRow row, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"{RestPath}?id=eq.{id}");
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task ExecuteDeleteAsync(HttpClient http, Guid id, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{RestPath}?id=eq.{id}");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record CourtRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("complex_id")] Guid ComplexId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc);
}
