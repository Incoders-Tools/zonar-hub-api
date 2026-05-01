using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.SocialNetworks;

public static class SocialNetworkErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "social_networks.not_found",
        "social_networks.errors.not_found");

    public static readonly Error NameRequired = Error.Validation(
        "social_networks.name_required",
        "social_networks.errors.name_required");

    public static readonly Error KeyRequired = Error.Validation(
        "social_networks.key_required",
        "social_networks.errors.key_required");

    public static readonly Error KeyAlreadyExists = Error.Conflict(
        "social_networks.key_exists",
        "social_networks.errors.key_exists");
}
