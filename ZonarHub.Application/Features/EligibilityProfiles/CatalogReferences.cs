using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Registrations;

namespace ZonarHub.Application.Features.EligibilityProfiles;

/// <summary>
/// Reads the gender and category catalogs once per request: players and eligibility slots may only reference existing
/// entries (an unknown id would otherwise fail the store's foreign key), and the D3 rules need the category levels and
/// the mixed gender.
/// </summary>
internal sealed class CatalogReferences(IGenderRepository genders, ICategoryRepository categories)
{
    /// <summary>Catalog key of the gender that does not restrict a slot.</summary>
    public const string MixedGenderKey = "mixed";

    public async Task<CatalogSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var genderRows = await genders.GetAllAsync(cancellationToken);
        var categoryRows = await categories.GetAllAsync(cancellationToken);
        return new CatalogSnapshot(
            genderRows.Select(g => g.Id).ToHashSet(),
            genderRows.Where(g => g.Key == MixedGenderKey).Select(g => g.Id).ToHashSet(),
            categoryRows.ToDictionary(c => c.Id, c => c.Level));
    }
}

/// <summary>The catalog entries one request validates against.</summary>
internal sealed record CatalogSnapshot(
    IReadOnlySet<Guid> GenderIds,
    IReadOnlySet<Guid> MixedGenderIds,
    IReadOnlyDictionary<Guid, int> CategoryLevels)
{
    /// <summary>True when no gender is given or the gender exists.</summary>
    public bool HasGender(Guid? id) => id is not { } value || GenderIds.Contains(value);

    /// <summary>True when no category is given or the category exists.</summary>
    public bool HasCategory(Guid? id) => id is not { } value || CategoryLevels.ContainsKey(value);

    public EligibilityCatalog ToEligibilityCatalog() => new(CategoryLevels, MixedGenderIds);
}
