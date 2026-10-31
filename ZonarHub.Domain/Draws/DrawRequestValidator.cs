using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Draws;

/// <summary>
/// Format-independent rules of a <see cref="DrawRequest"/>. Format-specific options are checked by each format.
/// </summary>
internal static class DrawRequestValidator
{
    public static Result Validate(DrawRequest request)
    {
        var participants = request.Participants;
        if (participants is null || participants.Count < DrawLimits.MinParticipants)
            return Result.Failure(DrawErrors.ParticipantsMin);

        if (participants.Count > DrawLimits.MaxParticipants)
            return Result.Failure(DrawErrors.ParticipantsMax);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var seedRanks = new HashSet<int>();
        foreach (var participant in participants)
        {
            if (participant is null)
                return Result.Failure(DrawErrors.ParticipantRequired);

            if (string.IsNullOrWhiteSpace(participant.Id))
                return Result.Failure(DrawErrors.ParticipantIdRequired);

            if (string.IsNullOrWhiteSpace(participant.DisplayName))
                return Result.Failure(DrawErrors.ParticipantNameRequired);

            if (!ids.Add(participant.Id))
                return Result.Failure(DrawErrors.DuplicateParticipant);

            if (participant.SeedRank is int seedRank)
            {
                if (seedRank < 1)
                    return Result.Failure(DrawErrors.InvalidSeedRank);

                if (!seedRanks.Add(seedRank))
                    return Result.Failure(DrawErrors.DuplicateSeedRank);
            }
        }

        if (!Enum.IsDefined(request.ParticipationFormat))
            return Result.Failure(DrawErrors.InvalidParticipationFormat);

        if (request.Randomization?.Seed is null)
            return Result.Failure(DrawErrors.SeedRequired);

        if (!Enum.IsDefined(request.Randomization.Mode))
            return Result.Failure(DrawErrors.InvalidRandomizationMode);

        return Result.Success();
    }
}
