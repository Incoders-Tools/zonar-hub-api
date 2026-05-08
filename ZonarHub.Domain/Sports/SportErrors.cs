using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Sports;

public static class SportErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "sports.not_found",
        "sports.errors.not_found");

    public static readonly Error NameRequired = Error.Validation(
        "sports.name_required",
        "sports.errors.name_required");

    public static readonly Error KeyRequired = Error.Validation(
        "sports.key_required",
        "sports.errors.key_required");

    public static readonly Error IconRequired = Error.Validation(
        "sports.icon_required",
        "sports.errors.icon_required");

    public static readonly Error KeyAlreadyExists = Error.Conflict(
        "sports.key_exists",
        "sports.errors.key_exists");

    public static readonly Error ModalityRequired = Error.Validation(
        "sports.modality_required",
        "sports.errors.modality_required");
}
