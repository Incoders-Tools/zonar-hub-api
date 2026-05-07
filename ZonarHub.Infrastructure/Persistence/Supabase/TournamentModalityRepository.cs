using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class TournamentModalityRepository : ITournamentModalityRepository
{
    private const string RestPath = "/rest/v1/tournament_modalities";

    private readonly HttpClient _http;

    public TournamentModalityRepository(IHttpClientFactory factory)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
    }

    public async Task<IReadOnlyList<TournamentModalityDto>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var url = includeInactive
            ? $"{RestPath}?select=*&order=sort_order.asc"
            : $"{RestPath}?select=*&is_active=eq.true&order=sort_order.asc";

        var rows = await ReadRowsAsync(url, cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public Task<IReadOnlyList<TournamentModalityDto>> GetAllActiveAsync(CancellationToken cancellationToken = default)
        => GetAllAsync(includeInactive: false, cancellationToken);

    public async Task<TournamentModalityDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await ReadRowsAsync($"{RestPath}?select=*&id=eq.{id}&limit=1", cancellationToken);
        return rows.Count == 0 ? null : ToDto(rows[0]);
    }

    public async Task<TournamentModalityDto?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var escaped = Uri.EscapeDataString(key.Trim().ToLowerInvariant());
        var rows = await ReadRowsAsync($"{RestPath}?select=*&key=eq.{escaped}&limit=1", cancellationToken);
        return rows.Count == 0 ? null : ToDto(rows[0]);
    }

    public async Task<TournamentModalityDto> AddAsync(
        TournamentModalityDto modality,
        CancellationToken cancellationToken = default)
    {
        var row = ToRow(modality);
        using var request = new HttpRequestMessage(HttpMethod.Post, RestPath);
        request.Headers.Add("Prefer", "return=representation");
        request.Content = JsonContent.Create(row);

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var inserted = await response.Content.ReadFromJsonAsync<List<ModalityRow>>(cancellationToken: cancellationToken);
        var first = inserted?.FirstOrDefault() ?? row;
        return ToDto(first);
    }

    public async Task<TournamentModalityDto> UpdateAsync(
        TournamentModalityDto modality,
        CancellationToken cancellationToken = default)
    {
        var patch = ToRow(modality);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{RestPath}?id=eq.{modality.Id}");
        request.Headers.Add("Prefer", "return=representation");
        request.Content = JsonContent.Create(patch);

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<List<ModalityRow>>(cancellationToken: cancellationToken);
        var first = updated?.FirstOrDefault() ?? patch;
        return ToDto(first);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync($"{RestPath}?id=eq.{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<IReadOnlyList<ModalityRow>> ReadRowsAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return [];

        var rows = await JsonSerializer.DeserializeAsync<List<ModalityRow>>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return rows ?? [];
    }

    private static TournamentModalityDto ToDto(ModalityRow row) => new(
        row.Id,
        row.NameEs,
        row.NameEn,
        row.NamePt,
        row.Key,
        row.SortOrder,
        row.IsActive);

    private static ModalityRow ToRow(TournamentModalityDto dto) => new(
        dto.Id,
        dto.NameEs,
        dto.NameEn,
        dto.NamePt,
        dto.Key,
        dto.SortOrder,
        dto.IsActive);

    private sealed record ModalityRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name_es")] string NameEs,
        [property: JsonPropertyName("name_en")] string NameEn,
        [property: JsonPropertyName("name_pt")] string NamePt,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("is_active")] bool IsActive);
}
