using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentModalities;

public static class TournamentModalityErrors
{
    public static readonly Error NotFound =
        Error.NotFound("TournamentModality.NotFound", "Tournament modality not found.");

    public static readonly Error KeyAlreadyExists =
        Error.Conflict("TournamentModality.KeyAlreadyExists", "Another modality already uses this key.");

    public static readonly Error NameRequired =
        Error.Validation("TournamentModality.NameRequired", "Modality name is required.");

    public static readonly Error KeyRequired =
        Error.Validation("TournamentModality.KeyRequired", "Modality key is required.");
}
