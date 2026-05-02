using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.FlyerBackgrounds;

public static class FlyerBackgroundErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "flyer_backgrounds.not_found",
        "flyer_backgrounds.errors.not_found");

    public static readonly Error DuplicateKey = Error.Conflict(
        "flyer_backgrounds.duplicate_key",
        "flyer_backgrounds.errors.duplicate_key");

    public static readonly Error NameRequired = Error.Validation(
        "flyer_backgrounds.name_required",
        "flyer_backgrounds.errors.name_required");

    public static readonly Error KeyRequired = Error.Validation(
        "flyer_backgrounds.key_required",
        "flyer_backgrounds.errors.key_required");

    public static readonly Error ImageUrlRequired = Error.Validation(
        "flyer_backgrounds.image_url_required",
        "flyer_backgrounds.errors.image_url_required");

    public static readonly Error CategoryRequired = Error.Validation(
        "flyer_backgrounds.category_required",
        "flyer_backgrounds.errors.category_required");
}
