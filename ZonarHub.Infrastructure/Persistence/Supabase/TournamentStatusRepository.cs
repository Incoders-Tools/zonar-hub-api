using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class TournamentStatusRepository : ITournamentStatusRepository
{
    private const string RestPath = "/rest/v1/tournament_statuses";

    private readonly HttpClient _http;

    public TournamentStatusRepository(IHttpClientFactory factory)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
    }

    public async Task<IReadOnlyList<TournamentStatusDto>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var url = includeInactive
            ? $"{RestPath}?select=*&order=sort_order.asc,name_es.asc"
            : $"{RestPath}?select=*&is_active=eq.true&order=sort_order.asc,name_es.asc";

        var rows = await ReadRowsAsync(url, cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<TournamentStatusDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await ReadRowsAsync($"{RestPath}?select=*&id=eq.{id}&limit=1", cancellationToken);
        return rows.Count == 0 ? null : ToDto(rows[0]);
    }

    public async Task<TournamentStatusDto?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var escaped = Uri.EscapeDataString(key.Trim().ToLowerInvariant());
        var rows = await ReadRowsAsync($"{RestPath}?select=*&key=eq.{escaped}&limit=1", cancellationToken);
        return rows.Count == 0 ? null : ToDto(rows[0]);
    }

    public async Task<TournamentStatusDto> AddAsync(
        TournamentStatusDto status,
        CancellationToken cancellationToken = default)
    {
        var row = ToRow(status);
        using var request = new HttpRequestMessage(HttpMethod.Post, RestPath);
        request.Headers.Add("Prefer", "return=representation");
        request.Content = JsonContent.Create(row);

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var inserted = await response.Content.ReadFromJsonAsync<List<StatusRow>>(cancellationToken: cancellationToken);
        var first = inserted?.FirstOrDefault() ?? row;
        return ToDto(first);
    }

    public async Task<TournamentStatusDto> UpdateAsync(
        TournamentStatusDto status,
        CancellationToken cancellationToken = default)
    {
        var patch = ToRow(status);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{RestPath}?id=eq.{status.Id}");
        request.Headers.Add("Prefer", "return=representation");
        request.Content = JsonContent.Create(patch);

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<List<StatusRow>>(cancellationToken: cancellationToken);
        var first = updated?.FirstOrDefault() ?? patch;
        return ToDto(first);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync($"{RestPath}?id=eq.{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<IReadOnlyList<StatusRow>> ReadRowsAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return [];

        var rows = await JsonSerializer.DeserializeAsync<List<StatusRow>>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return rows ?? [];
    }

    private static TournamentStatusDto ToDto(StatusRow row) => new(
        row.Id,
        row.Key,
        row.NameEs,
        row.NameEn,
        row.NamePt,
        row.DescriptionEs,
        row.DescriptionEn,
        row.DescriptionPt,
        row.SortOrder,
        row.IsActive,
        row.CreatedAtUtc,
        row.UpdatedAtUtc);

    private static StatusRow ToRow(TournamentStatusDto dto) => new(
        dto.Id,
        dto.Key,
        dto.NameEs,
        dto.NameEn,
        dto.NamePt,
        dto.DescriptionEs,
        dto.DescriptionEn,
        dto.DescriptionPt,
        dto.SortOrder,
        dto.IsActive,
        dto.CreatedAt,
        dto.UpdatedAt);

    private sealed record StatusRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("name_es")] string NameEs,
        [property: JsonPropertyName("name_en")] string NameEn,
        [property: JsonPropertyName("name_pt")] string NamePt,
        [property: JsonPropertyName("description_es")] string? DescriptionEs,
        [property: JsonPropertyName("description_en")] string? DescriptionEn,
        [property: JsonPropertyName("description_pt")] string? DescriptionPt,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);
}
