using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Draws;

/// <summary>
/// Pure round robin: one <see cref="PhaseKind.RoundRobin"/> phase (id <c>RR</c>) where everyone plays everyone.
/// Separation keys change nothing here: every pair meets regardless of the order.
/// </summary>
internal static class RoundRobin
{
    public const string PhaseId = "RR";

    private const int MinLegs = 1;
    private const int MaxLegs = 2;

    public static Result<Phase> Build(DrawRequest request, DeterministicRandom random)
    {
        var legs = request.Options?.RoundRobin?.Legs ?? MinLegs;
        if (legs is < MinLegs or > MaxLegs)
            return Result.Failure<Phase>(DrawErrors.InvalidRoundRobinLegs);

        var circle = SeedOrder.Arrange(request.Participants, request.Randomization.Mode, random)
            .Select(participant => participant.Id)
            .ToList();

        return Result.Success(new Phase(PhaseId, PhaseKind.RoundRobin, RoundRobinSchedule.Build(PhaseId, circle, legs)));
    }
}
