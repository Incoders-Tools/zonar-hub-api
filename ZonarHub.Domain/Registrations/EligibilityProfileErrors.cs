using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Registrations;

/// <summary>
/// Eligibility profile errors. <see cref="NameTaken"/> and <see cref="InUse"/> come from the store (unique name per
/// organization; a tournament still references the profile).
/// </summary>
public static class EligibilityProfileErrors
{
    public static readonly Error NotFound = Error.NotFound("eligibility_profiles.not_found", "eligibility_profiles.errors.not_found");
    public static readonly Error OrganizationIdRequired = Error.Validation("eligibility_profiles.organization_id_required", "eligibility_profiles.errors.organization_id_required");
    public static readonly Error NameRequired = Error.Validation("eligibility_profiles.name_required", "eligibility_profiles.errors.name_required");
    public static readonly Error NameTooLong = Error.Validation("eligibility_profiles.name_too_long", "eligibility_profiles.errors.name_too_long");
    public static readonly Error DescriptionTooLong = Error.Validation("eligibility_profiles.description_too_long", "eligibility_profiles.errors.description_too_long");
    public static readonly Error SlotsRequired = Error.Validation("eligibility_profiles.slots_required", "eligibility_profiles.errors.slots_required");
    public static readonly Error InvalidSlot = Error.Validation("eligibility_profiles.invalid_slot", "eligibility_profiles.errors.invalid_slot");
    public static readonly Error InvalidAgeRange = Error.Validation("eligibility_profiles.invalid_age_range", "eligibility_profiles.errors.invalid_age_range");
    public static readonly Error LabelTooLong = Error.Validation("eligibility_profiles.label_too_long", "eligibility_profiles.errors.label_too_long");
    public static readonly Error UnknownGender = Error.Validation("eligibility_profiles.unknown_gender", "eligibility_profiles.errors.unknown_gender");
    public static readonly Error UnknownCategory = Error.Validation("eligibility_profiles.unknown_category", "eligibility_profiles.errors.unknown_category");
    public static readonly Error NameTaken = Error.Conflict("eligibility_profiles.name_taken", "eligibility_profiles.errors.name_taken");
    public static readonly Error InUse = Error.Conflict("eligibility_profiles.in_use", "eligibility_profiles.errors.in_use");
}
