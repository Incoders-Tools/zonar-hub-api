using System.Numerics;
using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Draws;

/// <summary>
/// Single elimination: one knockout phase over every participant. Separation keys then keep participants of one key
/// out of the same first-round match when possible (<see cref="Separation.SeparateFirstRound"/>).
/// </summary>
internal static class SingleElimination
{
    private const int ThirdPlaceMinParticipants = 4;

    public static Result<Phase> Build(DrawRequest request, DeterministicRandom random)
    {
        var thirdPlaceMatch = request.Options?.Knockout?.ThirdPlaceMatch ?? false;
        if (thirdPlaceMatch && request.Participants.Count < ThirdPlaceMinParticipants)
            return Result.Failure<Phase>(DrawErrors.ThirdPlaceMinParticipants);

        var order = SeedOrder.Arrange(request.Participants, request.Randomization.Mode, random);
        var bracketSize = (int)BitOperations.RoundUpToPowerOf2((uint)order.Count);
        Separation.SeparateFirstRound(order, bracketSize, request.Randomization.Mode);

        var entrants = order.Select(participant => (Slot)new ParticipantSlot(participant.Id)).ToList();

        return Result.Success(KnockoutBracket.Build(KnockoutBracket.PhaseId, entrants, thirdPlaceMatch));
    }
}
