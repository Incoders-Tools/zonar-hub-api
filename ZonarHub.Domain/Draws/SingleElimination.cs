using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Draws;

/// <summary>
/// Single elimination: one knockout phase over every participant.
/// </summary>
internal static class SingleElimination
{
    private const int ThirdPlaceMinParticipants = 4;

    public static Result<Phase> Build(DrawRequest request, DeterministicRandom random)
    {
        var thirdPlaceMatch = request.Options?.Knockout?.ThirdPlaceMatch ?? false;
        if (thirdPlaceMatch && request.Participants.Count < ThirdPlaceMinParticipants)
            return Result.Failure<Phase>(DrawErrors.ThirdPlaceMinParticipants);

        var entrants = OrderBySeed(request.Participants, request.Randomization.Mode, random)
            .Select(participant => (Slot)new ParticipantSlot(participant.Id))
            .ToList();

        return Result.Success(KnockoutBracket.Build(KnockoutBracket.PhaseId, entrants, thirdPlaceMatch));
    }

    /// <summary>
    /// Random mode shuffles everyone. Seeded mode puts ranked participants first, by ascending rank, followed by the
    /// unranked ones shuffled.
    /// </summary>
    private static List<DrawParticipant> OrderBySeed(
        IReadOnlyList<DrawParticipant> participants,
        RandomizationMode mode,
        DeterministicRandom random)
    {
        if (mode == RandomizationMode.Random)
        {
            var everyone = participants.ToList();
            random.Shuffle(everyone);
            return everyone;
        }

        var seeded = participants.Where(p => p.SeedRank is not null).OrderBy(p => p.SeedRank).ToList();
        var unseeded = participants.Where(p => p.SeedRank is null).ToList();
        random.Shuffle(unseeded);
        return [.. seeded, .. unseeded];
    }
}
