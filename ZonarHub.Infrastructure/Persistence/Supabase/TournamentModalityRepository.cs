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

    public async Task<IReadOnlyList<TournamentModalityDto>> GetAllActiveAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{RestPath}?select=*&is_active=eq.true&order=sort_order.asc");

        using var response = await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return [];

        var rows = await JsonSerializer.DeserializeAsync<List<ModalityRow>>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken) ?? [];

        return rows.Select(r => new TournamentModalityDto(
            r.Id, r.NameEs, r.NameEn, r.NamePt, r.Key, r.SortOrder, r.IsActive)).ToList();
    }

    private sealed record ModalityRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name_es")] string NameEs,
        [property: JsonPropertyName("name_en")] string NameEn,
        [property: JsonPropertyName("name_pt")] string NamePt,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("is_active")] bool IsActive);
}
