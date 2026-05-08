using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class TournamentRuleRepository : ITournamentRuleRepository
{
    private const string RestPath = "/rest/v1/tournament_rules";

    private readonly HttpClient _http;

    public TournamentRuleRepository(IHttpClientFactory factory)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
    }

    public async Task<IReadOnlyList<TournamentRuleDto>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var url = includeInactive
            ? $"{RestPath}?select=*&order=sort_order.asc,name.asc"
            : $"{RestPath}?select=*&is_active=eq.true&order=sort_order.asc,name.asc";

        var rows = await ReadRowsAsync(url, cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<TournamentRuleDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await ReadRowsAsync($"{RestPath}?select=*&id=eq.{id}&limit=1", cancellationToken);
        return rows.Count == 0 ? null : ToDto(rows[0]);
    }

    public async Task<TournamentRuleDto> AddAsync(
        TournamentRuleDto rule,
        CancellationToken cancellationToken = default)
    {
        var row = ToRow(rule);
        using var request = new HttpRequestMessage(HttpMethod.Post, RestPath);
        request.Headers.Add("Prefer", "return=representation");
        request.Content = JsonContent.Create(row);

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var inserted = await response.Content.ReadFromJsonAsync<List<RuleRow>>(cancellationToken: cancellationToken);
        var first = inserted?.FirstOrDefault() ?? row;
        return ToDto(first);
    }

    public async Task<TournamentRuleDto> UpdateAsync(
        TournamentRuleDto rule,
        CancellationToken cancellationToken = default)
    {
        var patch = ToRow(rule);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{RestPath}?id=eq.{rule.Id}");
        request.Headers.Add("Prefer", "return=representation");
        request.Content = JsonContent.Create(patch);

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<List<RuleRow>>(cancellationToken: cancellationToken);
        var first = updated?.FirstOrDefault() ?? patch;
        return ToDto(first);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync($"{RestPath}?id=eq.{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<IReadOnlyList<RuleRow>> ReadRowsAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return [];

        var rows = await JsonSerializer.DeserializeAsync<List<RuleRow>>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return rows ?? [];
    }

    private static TournamentRuleDto ToDto(RuleRow row) => new(
        row.Id,
        row.Name,
        row.DescriptionEs,
        row.DescriptionEn,
        row.DescriptionPt,
        row.SortOrder,
        row.IsActive,
        row.CreatedAtUtc,
        row.UpdatedAtUtc);

    private static RuleRow ToRow(TournamentRuleDto dto) => new(
        dto.Id,
        dto.Name,
        dto.DescriptionEs,
        dto.DescriptionEn,
        dto.DescriptionPt,
        dto.SortOrder,
        dto.IsActive,
        dto.CreatedAt,
        dto.UpdatedAt);

    private sealed record RuleRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description_es")] string? DescriptionEs,
        [property: JsonPropertyName("description_en")] string? DescriptionEn,
        [property: JsonPropertyName("description_pt")] string? DescriptionPt,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);
}
