using ZonarHub.Domain.Common;
using ZonarHub.Domain.Registrations;

namespace ZonarHub.Application.Features.EligibilityProfiles;

/// <summary>
/// Body of <c>POST</c> and <c>PUT /api/admin/organizations/{organizationId}/eligibility-profiles</c> (D1): reusable
/// participant rules, one slot per participant.
/// </summary>
/// <param name="Name">Required, up to 120 characters, unique per organization (any case).</param>
/// <param name="Description">Optional, up to 500 characters.</param>
/// <param name="Slots">At least one slot; slot numbers 1..16 without repeats, returned in slot order.</param>
/// <param name="IsActive">Only read on update.</param>
public sealed record EligibilityProfileBody(
    string? Name,
    string? Description,
    IReadOnlyList<EligibilitySlotBody>? Slots,
    bool IsActive = true);

/// <summary>Rules of one participant slot; null rules do not restrict (D3).</summary>
/// <param name="GenderId">Required gender (<c>genders</c> catalog); the mixed gender admits any player.</param>
/// <param name="CategoryId">Required category (<c>categories</c> catalog), one level of tolerance.</param>
/// <param name="MinAge">Minimum age in whole years (0..120).</param>
/// <param name="MaxAge">Maximum age in whole years (0..120, not below <paramref name="MinAge"/>).</param>
/// <param name="Label">Display label, up to 60 characters.</param>
/// <param name="Required">False when the slot may stay empty.</param>
public sealed record EligibilitySlotBody(
    int SlotNumber,
    Guid? GenderId,
    Guid? CategoryId,
    int? MinAge,
    int? MaxAge,
    string? Label,
    bool Required = true);

/// <summary>An eligibility profile of the organization. Timestamps are UTC.</summary>
public sealed record EligibilityProfileResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<EligibilitySlotBody> Slots,
    DateTime CreatedAt,
    DateTime UpdatedAt);

internal static class EligibilityProfileMapper
{
    public static EligibilityProfileResponse ToResponse(EligibilityProfile profile) => new(
        profile.Id.Value,
        profile.OrganizationId.Value,
        profile.Name,
        profile.Description,
        profile.IsActive,
        profile.Slots.Select(s => new EligibilitySlotBody(s.SlotNumber, s.GenderId, s.CategoryId, s.MinAge, s.MaxAge, s.Label, s.Required)).ToList(),
        profile.CreatedAtUtc,
        profile.UpdatedAtUtc);

    public static IReadOnlyList<EligibilitySlot> ToSlots(EligibilityProfileBody body) =>
        (body.Slots ?? []).Select(s => new EligibilitySlot(s.SlotNumber, s.GenderId, s.CategoryId, s.MinAge, s.MaxAge, s.Label, s.Required)).ToList();

    /// <summary>
    /// Every slot's gender and category exist in the catalogs: <see cref="EligibilityProfileErrors.UnknownGender"/> or
    /// <see cref="EligibilityProfileErrors.UnknownCategory"/> otherwise.
    /// </summary>
    public static async Task<Result> CheckReferencesAsync(
        CatalogReferences catalogs,
        IReadOnlyList<EligibilitySlot> slots,
        CancellationToken cancellationToken)
    {
        if (slots.All(s => s.GenderId is null && s.CategoryId is null))
            return Result.Success();

        var catalog = await catalogs.LoadAsync(cancellationToken);
        if (!slots.All(s => catalog.HasGender(s.GenderId)))
            return Result.Failure(EligibilityProfileErrors.UnknownGender);

        return slots.All(s => catalog.HasCategory(s.CategoryId))
            ? Result.Success()
            : Result.Failure(EligibilityProfileErrors.UnknownCategory);
    }
}
