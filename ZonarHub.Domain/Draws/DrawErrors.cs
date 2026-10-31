using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Draws;

public static class DrawErrors
{
    public static readonly Error ParticipantsMin = Validation("participants_min");
    public static readonly Error ParticipantsMax = Validation("participants_max");
    public static readonly Error ParticipantRequired = Validation("participant_required");
    public static readonly Error ParticipantIdRequired = Validation("participant_id_required");
    public static readonly Error ParticipantNameRequired = Validation("participant_name_required");
    public static readonly Error DuplicateParticipant = Validation("duplicate_participant");
    public static readonly Error InvalidSeedRank = Validation("invalid_seed_rank");
    public static readonly Error DuplicateSeedRank = Validation("duplicate_seed_rank");
    public static readonly Error SeedRequired = Validation("seed_required");
    public static readonly Error InvalidRandomizationMode = Validation("invalid_randomization_mode");
    public static readonly Error InvalidParticipationFormat = Validation("invalid_participation_format");
    public static readonly Error FormatNotSupported = Validation("format_not_supported");
    public static readonly Error ThirdPlaceMinParticipants = Validation("third_place_min_participants");

    private static Error Validation(string name) => Error.Validation($"draws.{name}", $"draws.errors.{name}");
}
