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

        var entrants = SeedOrder.Arrange(request.Participants, request.Randomization.Mode, random)
            .Select(participant => (Slot)new ParticipantSlot(participant.Id))
            .ToList();

        return Result.Success(KnockoutBracket.Build(KnockoutBracket.PhaseId, entrants, thirdPlaceMatch));
    }
}
