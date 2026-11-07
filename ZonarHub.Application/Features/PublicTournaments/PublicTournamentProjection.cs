using ZonarHub.Application.Abstractions;

namespace ZonarHub.Application.Features.PublicTournaments;

/// <summary>
/// Turns showcase records into the public contract: resolves catalog display names (categories, genders,
/// modalities, statuses have no foreign key to embed, so the small catalogs are read once per request) and applies
/// the organization consent rule.
/// </summary>
internal sealed class PublicTournamentProjection(
    ICategoryRepository categories,
    IGenderRepository genders,
    ITournamentModalityRepository modalities,
    ITournamentStatusRepository statuses)
{
    public async Task<IReadOnlyList<PublicTournamentResponse>> ProjectAsync(
        IReadOnlyList<PublicTournamentRecord> records,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return [];
        }

        var categoryById = (await categories.GetAllAsync(cancellationToken)).ToDictionary(c => c.Id);
        var genderById = (await genders.GetAllAsync(cancellationToken)).ToDictionary(g => g.Id);
        var modalityById = (await modalities.GetAllAsync(includeInactive: true, cancellationToken)).ToDictionary(m => m.Id);
        var statusByKey = (await statuses.GetAllAsync(includeInactive: true, cancellationToken))
            .ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);

        return records.Select(record => new PublicTournamentResponse(
            record.Slug,
            record.Name,
            record.Description,
            record.Rules,
            record.StartDate,
            record.EndDate,
            record.RegistrationStartDate,
            record.RegistrationEndDate,
            ToStatus(record.Status, statusByKey),
            record.Sport is null ? null : new PublicSportResponse(record.Sport.Key, record.Sport.Name, record.Sport.Icon, record.Sport.IconSource),
            Lookup(categoryById, record.CategoryId) is { } category ? new PublicCategoryResponse(category.Key, category.Name, category.ShortName) : null,
            Lookup(genderById, record.GenderId) is { } gender ? new PublicGenderResponse(gender.Key, gender.Name) : null,
            Lookup(modalityById, record.ModalityId) is { } modality ? new PublicModalityResponse(modality.Key, modality.NameEs, modality.NameEn, modality.NamePt) : null,
            record.Complex is null ? null : new PublicComplexResponse(record.Complex.Name, record.Complex.Address),
            record.ImageUrl,
            record.CoverImageUrl,
            record.MaxPairs,
            record.RegistrationFeePerPair,
            record.PrizeMoney,
            ToOrganization(record.Organization))).ToList();
    }

    private static TValue? Lookup<TValue>(Dictionary<Guid, TValue> byId, Guid? id) where TValue : class =>
        id is { } value && byId.TryGetValue(value, out var item) ? item : null;

    private static PublicTournamentStatusResponse? ToStatus(string? key, Dictionary<string, TournamentStatusDto> byKey)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return byKey.TryGetValue(key, out var status)
            ? new PublicTournamentStatusResponse(key, status.NameEs, status.NameEn, status.NamePt)
            : new PublicTournamentStatusResponse(key, null, null, null);
    }

    /// <summary>
    /// The organization is shown only when it is active and consented to a public profile; otherwise none of its
    /// data leaves the API. Blank links were already normalized to null by the profile and are omitted from JSON.
    /// </summary>
    private static PublicOrganizationResponse? ToOrganization(PublicOrganizationRecord? organization)
    {
        if (organization is null || !organization.IsActive || !organization.Profile.Enabled)
        {
            return null;
        }

        var profile = organization.Profile;
        return new PublicOrganizationResponse(
            organization.DisplayName,
            organization.LogoUrl,
            organization.Description,
            organization.Type,
            new PublicOrganizationLinksResponse(
                profile.WebsiteUrl,
                profile.InstagramUrl,
                profile.FacebookUrl,
                profile.TiktokUrl,
                profile.YoutubeUrl,
                profile.WhatsappPhone,
                profile.ContactEmail));
    }
}
