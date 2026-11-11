using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Registrations;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

/// <summary>
/// PostgREST store of eligibility profiles and their slots (migration <c>20261110100000_tournament_registrations</c>).
/// Reads embed the slots; a save is one call to <c>save_tournament_eligibility_profile</c>, which writes the profile and
/// replaces every slot in one transaction. Store refusals become <see cref="PersistenceConflictException"/>
/// (<see cref="EligibilityProfileErrors.NotFound"/>, <see cref="EligibilityProfileErrors.NameTaken"/>,
/// <see cref="EligibilityProfileErrors.InUse"/>); other failures throw <see cref="HttpRequestException"/>.
/// </summary>
internal sealed class EligibilityProfileRepository(IHttpClientFactory factory) : IEligibilityProfileRepository
{
    private const string RestPath = "/rest/v1/tournament_eligibility_profiles";
    private const string TournamentsPath = "/rest/v1/tournaments";
    private const string SaveRpc = "/rest/v1/rpc/save_tournament_eligibility_profile";
    private const string WithSlots = "select=*,slots:tournament_eligibility_profile_slots(*)";
    private const string SlotsOrder = "slots.order=slot_number.asc";

    private readonly HttpClient _http = factory.CreateClient(SupabaseHttpClientName.Name);

    public async Task<IReadOnlyList<EligibilityProfile>> ListByOrganizationAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await GetRowsAsync($"{WithSlots}&organization_id=eq.{organizationId}&order=name.asc,id.asc&{SlotsOrder}", cancellationToken);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<EligibilityProfile?> GetByIdAsync(
        OrganizationId organizationId,
        EligibilityProfileId id,
        CancellationToken cancellationToken = default)
    {
        var rows = await GetRowsAsync($"{WithSlots}&organization_id=eq.{organizationId}&id=eq.{id}&{SlotsOrder}&limit=1", cancellationToken);
        return rows.Count == 0 ? null : ToDomain(rows[0]);
    }

    public async Task<EligibilityProfileId?> GetTournamentProfileIdAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
            $"{TournamentsPath}?select=eligibility_profile_id&id=eq.{tournamentId}&limit=1", cancellationToken);
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<List<TournamentProfileRow>>(cancellationToken) ?? [];
        return rows.FirstOrDefault()?.EligibilityProfileId is { } id ? new EligibilityProfileId(id) : null;
    }

    public async Task<EligibilityProfile> SaveAsync(EligibilityProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var parameters = new SaveParameters(
            profile.Id.Value,
            profile.OrganizationId.Value,
            profile.Name,
            profile.Description,
            profile.IsActive,
            profile.Slots.Select(s => new SlotRow(s.SlotNumber, s.GenderId, s.CategoryId, s.MinAge, s.MaxAge, s.Label, s.Required)).ToList(),
            DateTime.SpecifyKind(profile.UpdatedAtUtc, DateTimeKind.Utc));

        using var response = await _http.PostAsJsonAsync(SaveRpc, parameters, cancellationToken);
        if (await SupabaseResponses.ReadConflictAsync(response, cancellationToken) is { } conflict)
        {
            if (conflict.Code == SupabaseResponses.RefusedWriteCode && conflict.Message == EligibilityProfileErrors.NotFound.Code)
                throw new PersistenceConflictException(EligibilityProfileErrors.NotFound);
            if (conflict.Violates(SupabaseResponses.UniqueViolationCode, "ux_tournament_eligibility_profiles_organization_name"))
                throw new PersistenceConflictException(EligibilityProfileErrors.NameTaken);
        }

        response.EnsureSuccessStatusCode();
        var row = await response.Content.ReadFromJsonAsync<ProfileRow>(cancellationToken)
            ?? throw new JsonException($"The function {SaveRpc} returned no result.");
        return ToDomain(row);
    }

    public async Task<bool> DeleteAsync(OrganizationId organizationId, EligibilityProfileId id, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{RestPath}?id=eq.{id}&organization_id=eq.{organizationId}&select=id");
        request.Headers.Add("Prefer", "return=representation");
        using var response = await _http.SendAsync(request, cancellationToken);
        if (await SupabaseResponses.ReadConflictAsync(response, cancellationToken) is { } conflict
            && conflict.Violates(SupabaseResponses.ForeignKeyViolationCode, "fk_tournaments_eligibility_profile"))
            throw new PersistenceConflictException(EligibilityProfileErrors.InUse);

        response.EnsureSuccessStatusCode();
        var deleted = await response.Content.ReadFromJsonAsync<List<JsonElement>>(cancellationToken) ?? [];
        return deleted.Count > 0;
    }

    private async Task<List<ProfileRow>> GetRowsAsync(string query, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{RestPath}?{query}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ProfileRow>>(cancellationToken) ?? [];
    }

    private static EligibilityProfile ToDomain(ProfileRow row) => EligibilityProfile.Reconstitute(
        new EligibilityProfileId(row.Id),
        new OrganizationId(row.OrganizationId),
        row.Name,
        row.Description,
        row.IsActive,
        (row.Slots ?? []).Select(s => new EligibilitySlot(s.SlotNumber, s.GenderId, s.CategoryId, s.MinAge, s.MaxAge, s.Label, s.Required)).ToList(),
        row.CreatedAtUtc.UtcDateTime,
        row.UpdatedAtUtc.UtcDateTime);

    private sealed record TournamentProfileRow(
        [property: JsonPropertyName("eligibility_profile_id")] Guid? EligibilityProfileId);

    private sealed record SaveParameters(
        [property: JsonPropertyName("p_profile_id")] Guid ProfileId,
        [property: JsonPropertyName("p_organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("p_name")] string Name,
        [property: JsonPropertyName("p_description")] string? Description,
        [property: JsonPropertyName("p_is_active")] bool IsActive,
        [property: JsonPropertyName("p_slots")] IReadOnlyList<SlotRow> Slots,
        [property: JsonPropertyName("p_now_utc")] DateTime NowUtc);

    private sealed record SlotRow(
        [property: JsonPropertyName("slot_number")] int SlotNumber,
        [property: JsonPropertyName("gender_id")] Guid? GenderId,
        [property: JsonPropertyName("category_id")] Guid? CategoryId,
        [property: JsonPropertyName("min_age")] int? MinAge,
        [property: JsonPropertyName("max_age")] int? MaxAge,
        [property: JsonPropertyName("label")] string? Label,
        [property: JsonPropertyName("required")] bool Required);

    private sealed record ProfileRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
        [property: JsonPropertyName("slots")] List<SlotRow>? Slots = null);
}
