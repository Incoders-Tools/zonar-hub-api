using System.Text.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

/// <summary>
/// PostgREST read side of the public tournament showcase. The showcase filter, ordering and limit run in the
/// database; sport, complex and organization are embedded through their foreign keys. Only public-safe columns are
/// selected, so admin-only data (observations, courts, points, sum value, owner ids) never leaves the store.
/// </summary>
internal sealed class PublicTournamentReadRepository : IPublicTournamentReadRepository
{
    private const string RestPath = "/rest/v1/tournaments";

    private const string Select =
        "select=public_slug,name,description,rules,start_date,end_date,registration_start_date,registration_end_date," +
        "status,category_id,gender_id,modality_id,max_pairs,image_url,cover_image_url,registration_fee_per_pair,prize_money," +
        "sport:sports(key,name,icon,icon_source)," +
        "complex:complexes(name,address)," +
        "organization:organizations(display_name,logo_url,description,type,is_active,public_profile_enabled," +
        "website_url,instagram_url,facebook_url,tiktok_url,youtube_url,whatsapp_phone,contact_email)";

    /// <summary>
    /// Public, active, shareable and not cancelled. A missing status is not cancelled, and PostgREST's
    /// <c>neq</c> alone would drop it (SQL null semantics), hence the explicit <c>or</c>.
    /// </summary>
    private const string ShowcaseFilter =
        "is_public=eq.true&is_active=eq.true&public_slug=not.is.null&or=(status.is.null,status.neq.cancelled)";

    private readonly HttpClient _http;

    public PublicTournamentReadRepository(IHttpClientFactory factory)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
    }

    public async Task<IReadOnlyList<PublicTournamentRecord>> ListAsync(int limit, CancellationToken cancellationToken = default)
    {
        var url = $"{RestPath}?{Select}&{ShowcaseFilter}&order=start_date.asc,name.asc&limit={limit}";
        var rows = await ReadRowsAsync(url, cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<PublicTournamentRecord?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var url = $"{RestPath}?{Select}&{ShowcaseFilter}&public_slug=eq.{SupabaseQuery.Value(slug)}&limit=1";
        var rows = await ReadRowsAsync(url, cancellationToken);
        return rows.Count == 0 ? null : ToRecord(rows[0]);
    }

    private async Task<IReadOnlyList<PublicTournamentRow>> ReadRowsAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rows = await JsonSerializer.DeserializeAsync<List<PublicTournamentRow>>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return rows ?? [];
    }

    private static PublicTournamentRecord ToRecord(PublicTournamentRow row) => new(
        row.PublicSlug,
        row.Name,
        row.Description,
        row.Rules,
        row.StartDate,
        row.EndDate,
        row.RegistrationStartDate,
        row.RegistrationEndDate,
        row.Status,
        row.CategoryId,
        row.GenderId,
        row.ModalityId,
        row.MaxPairs,
        row.ImageUrl,
        row.CoverImageUrl,
        row.RegistrationFeePerPair,
        row.PrizeMoney,
        row.Sport is null ? null : new PublicSportRecord(row.Sport.Key, row.Sport.Name, row.Sport.Icon, row.Sport.IconSource),
        row.Complex is null ? null : new PublicComplexRecord(row.Complex.Name, row.Complex.Address),
        row.Organization is null ? null : ToRecord(row.Organization));

    private static PublicOrganizationRecord ToRecord(OrganizationEmbed o) => new(
        o.DisplayName,
        o.LogoUrl,
        o.Description,
        o.Type,
        o.IsActive,
        OrganizationPublicProfile.Create(
            o.PublicProfileEnabled,
            o.WebsiteUrl,
            o.InstagramUrl,
            o.FacebookUrl,
            o.TiktokUrl,
            o.YoutubeUrl,
            o.WhatsappPhone,
            o.ContactEmail));

    private sealed record PublicTournamentRow(
        [property: JsonPropertyName("public_slug")] string PublicSlug,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("rules")] string? Rules,
        [property: JsonPropertyName("start_date")] DateOnly StartDate,
        [property: JsonPropertyName("end_date")] DateOnly EndDate,
        [property: JsonPropertyName("registration_start_date")] DateOnly? RegistrationStartDate,
        [property: JsonPropertyName("registration_end_date")] DateOnly? RegistrationEndDate,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("category_id")] Guid? CategoryId,
        [property: JsonPropertyName("gender_id")] Guid? GenderId,
        [property: JsonPropertyName("modality_id")] Guid? ModalityId,
        [property: JsonPropertyName("max_pairs")] int? MaxPairs,
        [property: JsonPropertyName("image_url")] string? ImageUrl,
        [property: JsonPropertyName("cover_image_url")] string? CoverImageUrl,
        [property: JsonPropertyName("registration_fee_per_pair")] decimal? RegistrationFeePerPair,
        [property: JsonPropertyName("prize_money")] decimal? PrizeMoney,
        [property: JsonPropertyName("sport")] SportEmbed? Sport,
        [property: JsonPropertyName("complex")] ComplexEmbed? Complex,
        [property: JsonPropertyName("organization")] OrganizationEmbed? Organization);

    private sealed record SportEmbed(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("icon")] string Icon,
        [property: JsonPropertyName("icon_source")] string IconSource);

    private sealed record ComplexEmbed(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("address")] string? Address);

    private sealed record OrganizationEmbed(
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("logo_url")] string? LogoUrl,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("public_profile_enabled")] bool PublicProfileEnabled = false,
        [property: JsonPropertyName("website_url")] string? WebsiteUrl = null,
        [property: JsonPropertyName("instagram_url")] string? InstagramUrl = null,
        [property: JsonPropertyName("facebook_url")] string? FacebookUrl = null,
        [property: JsonPropertyName("tiktok_url")] string? TiktokUrl = null,
        [property: JsonPropertyName("youtube_url")] string? YoutubeUrl = null,
        [property: JsonPropertyName("whatsapp_phone")] string? WhatsappPhone = null,
        [property: JsonPropertyName("contact_email")] string? ContactEmail = null);
}
