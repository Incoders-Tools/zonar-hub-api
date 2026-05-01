using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class ComplexRepository : IComplexRepository
{
    private const string RestPath = "/rest/v1/complexes";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public ComplexRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<Complex?> GetByIdAsync(ComplexId id, CancellationToken cancellationToken = default)
    {
        var rows = await _http.GetFromJsonAsync<List<ComplexRow>>(
            $"{RestPath}?select=*&id=eq.{id.Value}&limit=1",
            cancellationToken);

        var row = rows?.FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public Task AddAsync(Complex complex, CancellationToken cancellationToken = default)
    {
        var row = ToRow(complex);
        _ops.Enqueue((http, ct) => ExecuteAddAsync(http, row, ct));
        return Task.CompletedTask;
    }

    private static Complex ToDomain(ComplexRow row) =>
        Complex.Reconstitute(
            new ComplexId(row.Id),
            new OrganizationId(row.OrganizationId),
            row.Name,
            row.Address,
            row.Location,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static ComplexRow ToRow(Complex complex) =>
        new(
            complex.Id.Value,
            complex.OrganizationId.Value,
            complex.Name,
            complex.Address,
            complex.Location,
            complex.IsActive,
            complex.CreatedAtUtc,
            complex.UpdatedAtUtc);

    private static async Task ExecuteAddAsync(HttpClient http, ComplexRow row, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RestPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record ComplexRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("location")] string? Location,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);
}
