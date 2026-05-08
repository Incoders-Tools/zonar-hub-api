using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class TournamentAdminRepository : ITournamentAdminRepository
{
    private const string RestPath = "/rest/v1/tournaments";

    private readonly HttpClient _http;

    public TournamentAdminRepository(IHttpClientFactory factory)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
    }

    public async Task<IReadOnlyList<TournamentAdminDto>> ListByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var url = $"{RestPath}?select=*&organization_id=eq.{organizationId}&order=start_date.desc";
        var rows = await ReadRowsAsync(url, cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<TournamentAdminDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await ReadRowsAsync($"{RestPath}?select=*&id=eq.{id}&limit=1", cancellationToken);
        return rows.Count == 0 ? null : ToDto(rows[0]);
    }

    public async Task<TournamentAdminDto> AddAsync(
        TournamentAdminDto tournament,
        CancellationToken cancellationToken = default)
    {
        var row = ToRow(tournament);
        using var request = new HttpRequestMessage(HttpMethod.Post, RestPath);
        request.Headers.Add("Prefer", "return=representation");
        request.Content = JsonContent.Create(row);

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var inserted = await response.Content.ReadFromJsonAsync<List<TournamentRow>>(cancellationToken: cancellationToken);
        var first = inserted?.FirstOrDefault() ?? row;
        return ToDto(first);
    }

    public async Task<TournamentAdminDto> UpdateAsync(
        TournamentAdminDto tournament,
        CancellationToken cancellationToken = default)
    {
        var patch = ToRow(tournament);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{RestPath}?id=eq.{tournament.Id}");
        request.Headers.Add("Prefer", "return=representation");
        request.Content = JsonContent.Create(patch);

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<List<TournamentRow>>(cancellationToken: cancellationToken);
        var first = updated?.FirstOrDefault() ?? patch;
        return ToDto(first);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync($"{RestPath}?id=eq.{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<IReadOnlyList<TournamentRow>> ReadRowsAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return [];

        var rows = await JsonSerializer.DeserializeAsync<List<TournamentRow>>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return rows ?? [];
    }

    private static TournamentAdminDto ToDto(TournamentRow row) => new(
        row.Id,
        row.OrganizationId,
        row.Name,
        row.Key,
        row.ComplexId,
        row.SportId,
        row.CategoryId,
        row.GenderId,
        row.ModalityId,
        row.TournamentTypeId,
        row.RuleSetId,
        row.Status,
        row.StartDate,
        row.EndDate,
        row.RegistrationStartDate,
        row.RegistrationEndDate,
        row.MaxPairs,
        row.Description,
        row.Rules,
        row.ImageUrl,
        row.CoverImageUrl,
        row.RegistrationFeePerPair,
        row.PrizeMoney,
        row.PointsToAward,
        row.SumValue,
        row.Observations,
        row.IsActive,
        row.SelectedCourtIds ?? [],
        row.CreatedAtUtc,
        row.UpdatedAtUtc);

    private static TournamentRow ToRow(TournamentAdminDto dto) => new(
        dto.Id,
        dto.OrganizationId,
        dto.Name,
        dto.Key,
        dto.ComplexId,
        dto.SportId,
        dto.CategoryId,
        dto.GenderId,
        dto.ModalityId,
        dto.TournamentTypeId,
        dto.RuleSetId,
        dto.Status,
        dto.StartDate,
        dto.EndDate,
        dto.RegistrationStartDate,
        dto.RegistrationEndDate,
        dto.MaxPairs,
        dto.Description,
        dto.Rules,
        dto.ImageUrl,
        dto.CoverImageUrl,
        dto.RegistrationFeePerPair,
        dto.PrizeMoney,
        dto.PointsToAward,
        dto.SumValue,
        dto.Observations,
        dto.IsActive,
        dto.SelectedCourtIds.ToList(),
        dto.CreatedAtUtc,
        dto.UpdatedAtUtc);

    private sealed record TournamentRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("key")] string? Key,
        [property: JsonPropertyName("complex_id")] Guid? ComplexId,
        [property: JsonPropertyName("sport_id")] Guid SportId,
        [property: JsonPropertyName("category_id")] Guid? CategoryId,
        [property: JsonPropertyName("gender_id")] Guid? GenderId,
        [property: JsonPropertyName("modality_id")] Guid? ModalityId,
        [property: JsonPropertyName("tournament_type_id")] Guid? TournamentTypeId,
        [property: JsonPropertyName("rule_set_id")] Guid? RuleSetId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("start_date")] DateOnly StartDate,
        [property: JsonPropertyName("end_date")] DateOnly EndDate,
        [property: JsonPropertyName("registration_start_date")] DateOnly? RegistrationStartDate,
        [property: JsonPropertyName("registration_end_date")] DateOnly? RegistrationEndDate,
        [property: JsonPropertyName("max_pairs")] int? MaxPairs,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("rules")] string? Rules,
        [property: JsonPropertyName("image_url")] string? ImageUrl,
        [property: JsonPropertyName("cover_image_url")] string? CoverImageUrl,
        [property: JsonPropertyName("registration_fee_per_pair")] decimal? RegistrationFeePerPair,
        [property: JsonPropertyName("prize_money")] decimal? PrizeMoney,
        [property: JsonPropertyName("points_to_award")] int? PointsToAward,
        [property: JsonPropertyName("sum_value")] decimal? SumValue,
        [property: JsonPropertyName("observations")] string? Observations,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("selected_court_ids")] List<Guid>? SelectedCourtIds,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);
}
