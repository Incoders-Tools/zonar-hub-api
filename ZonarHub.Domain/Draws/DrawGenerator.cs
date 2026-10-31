using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Draws;

/// <summary>
/// Entry point of the draw engine. Pure and deterministic: the same request, including its seed, always yields the
/// same draw.
/// </summary>
public static class DrawGenerator
{
    /// <summary>
    /// Validates <paramref name="request"/> and generates its draw, or returns a <see cref="DrawErrors"/> validation
    /// error. An unknown <see cref="DrawFormat"/> returns <see cref="DrawErrors.FormatNotSupported"/>.
    /// </summary>
    public static Result<Draw> Generate(DrawRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = DrawRequestValidator.Validate(request);
        if (validation.IsFailure)
            return Result.Failure<Draw>(validation.Error);

        var seed = request.Randomization.Seed!.Value;
        var random = new DeterministicRandom(seed);

        var phases = request.Format switch
        {
            DrawFormat.SingleElimination => Single(SingleElimination.Build(request, random)),
            DrawFormat.RoundRobin => Single(RoundRobin.Build(request, random)),
            DrawFormat.GroupsThenKnockout => GroupsThenKnockout.Build(request, random),
            _ => Result.Failure<IReadOnlyList<Phase>>(DrawErrors.FormatNotSupported),
        };

        if (phases.IsFailure)
            return Result.Failure<Draw>(phases.Error);

        return Result.Success(new Draw(
            request.Format,
            request.ParticipationFormat,
            request.Randomization.Mode,
            seed,
            request.Participants.ToList().AsReadOnly(),
            phases.Value));
    }

    private static Result<IReadOnlyList<Phase>> Single(Result<Phase> phase) =>
        phase.IsFailure
            ? Result.Failure<IReadOnlyList<Phase>>(phase.Error)
            : Result.Success<IReadOnlyList<Phase>>(new[] { phase.Value }.AsReadOnly());
}
