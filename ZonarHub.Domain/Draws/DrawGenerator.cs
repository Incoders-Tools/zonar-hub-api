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
    /// error. <see cref="DrawFormat.GroupsThenKnockout"/> is not supported yet and returns
    /// <see cref="DrawErrors.FormatNotSupported"/>.
    /// </summary>
    public static Result<Draw> Generate(DrawRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = DrawRequestValidator.Validate(request);
        if (validation.IsFailure)
            return Result.Failure<Draw>(validation.Error);

        var seed = request.Randomization.Seed!.Value;
        var random = new DeterministicRandom(seed);

        var phase = request.Format switch
        {
            DrawFormat.SingleElimination => SingleElimination.Build(request, random),
            DrawFormat.RoundRobin => RoundRobin.Build(request, random),
            _ => Result.Failure<Phase>(DrawErrors.FormatNotSupported),
        };

        if (phase.IsFailure)
            return Result.Failure<Draw>(phase.Error);

        return Result.Success(new Draw(
            request.Format,
            request.ParticipationFormat,
            request.Randomization.Mode,
            seed,
            request.Participants.ToList().AsReadOnly(),
            new[] { phase.Value }.AsReadOnly()));
    }
}
