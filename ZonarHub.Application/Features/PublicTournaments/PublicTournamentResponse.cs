using System.Text.Json.Serialization;

namespace ZonarHub.Application.Features.PublicTournaments;

/// <summary>
/// Anonymous, read-only view of a public tournament. Carries no internal ids and no admin-only fields.
/// </summary>
/// <param name="Slug">Stable share-link slug; the tournament is addressed as <c>/api/public/tournaments/{slug}</c>.</param>
/// <param name="Status">Status catalog key with its localized names; null when the tournament has no status.</param>
/// <param name="Organization">Owning organization; null unless it is active and consented to a public profile.</param>
public sealed record PublicTournamentResponse(
    string Slug,
    string Name,
    string? Description,
    string? Rules,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly? RegistrationStartDate,
    DateOnly? RegistrationEndDate,
    PublicTournamentStatusResponse? Status,
    PublicSportResponse? Sport,
    PublicCategoryResponse? Category,
    PublicGenderResponse? Gender,
    PublicModalityResponse? Modality,
    PublicComplexResponse? Complex,
    string? ImageUrl,
    string? CoverImageUrl,
    int? MaxPairs,
    decimal? RegistrationFeePerPair,
    decimal? PrizeMoney,
    PublicOrganizationResponse? Organization);

/// <summary>Localized names are null when the key is not in the status catalog.</summary>
public sealed record PublicTournamentStatusResponse(string Key, string? NameEs, string? NameEn, string? NamePt);

/// <param name="IconSource"><c>unicode</c> or <c>svg</c>.</param>
public sealed record PublicSportResponse(string Key, string Name, string Icon, string IconSource);

public sealed record PublicCategoryResponse(string Key, string Name, string ShortName);

public sealed record PublicGenderResponse(string Key, string Name);

public sealed record PublicModalityResponse(string Key, string NameEs, string NameEn, string NamePt);

public sealed record PublicComplexResponse(string Name, string? Address);

/// <param name="Type">Organization type key (<c>estandar</c>, <c>circuito</c>, <c>academia</c>, <c>operadora</c>, <c>marca</c>).</param>
public sealed record PublicOrganizationResponse(
    string DisplayName,
    string? LogoUrl,
    string? Description,
    string Type,
    PublicOrganizationLinksResponse Links);

/// <summary>Contact links of the organization. Empty links are omitted from the JSON.</summary>
public sealed record PublicOrganizationLinksResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WebsiteUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InstagramUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FacebookUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TiktokUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? YoutubeUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WhatsappPhone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ContactEmail);

public sealed record PublicTournamentListResponse(IReadOnlyList<PublicTournamentResponse> Items);
