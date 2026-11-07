using System.Globalization;
using System.Security.Cryptography;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Draws.Results;

namespace ZonarHub.Application.Features.TournamentDraws;

/// <summary>A draw request translated to the domain: the request as submitted and its scoring rules.</summary>
/// <param name="Request">Request as submitted; its seed is null when the client sent none.</param>
/// <param name="Scoring">Scoring rules of the draw (best of 3).</param>
/// <param name="Seed">Seed to generate with: the submitted one, or a freshly generated one.</param>
public sealed record DrawSubmission(DrawRequest Request, ScoringRules Scoring, ulong Seed)
{
    /// <summary>The request with <see cref="Seed"/> applied, ready for <see cref="DrawGenerator.Generate"/>.</summary>
    public DrawRequest Seeded => Request with { Randomization = Request.Randomization with { Seed = Seed } };
}

/// <summary>
/// Anti-corruption step between the draw request/result bodies (wire names, decimal seeds, offsets) and the domain.
/// Engine rules (participants, options) stay in the domain; this only rejects values the domain cannot represent.
/// </summary>
public static class TournamentDrawRequestMapper
{
    /// <summary>
    /// Translates a draw request body. Unknown enum names fail with the matching <see cref="DrawErrors"/> code; a seed
    /// that is not an unsigned 64-bit decimal fails with <see cref="TournamentDrawErrors.InvalidSeed"/>; an absent or
    /// blank seed is generated with a cryptographic random number generator (the only non-deterministic step, D5).
    /// </summary>
    public static Result<DrawSubmission> ToSubmission(TournamentDrawRequestBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!DrawWireNames.TryParse(body.Format, out DrawFormat format))
            return Result.Failure<DrawSubmission>(DrawErrors.FormatNotSupported);

        if (!DrawWireNames.TryParse(body.ParticipationFormat, out ParticipationFormat participationFormat))
            return Result.Failure<DrawSubmission>(DrawErrors.InvalidParticipationFormat);

        if (!DrawWireNames.TryParse(body.Randomization?.Mode, out RandomizationMode mode))
            return Result.Failure<DrawSubmission>(DrawErrors.InvalidRandomizationMode);

        ulong? submittedSeed = null;
        if (!string.IsNullOrWhiteSpace(body.Randomization!.Seed))
        {
            if (!ulong.TryParse(body.Randomization.Seed.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                return Result.Failure<DrawSubmission>(TournamentDrawErrors.InvalidSeed);

            submittedSeed = parsed;
        }

        var options = body.Options;
        var request = new DrawRequest(
            format,
            participationFormat,
            (body.Participants ?? [])
                .Select(participant => participant is null
                    ? null!
                    : new DrawParticipant(
                        participant.Id!,
                        participant.DisplayName!,
                        participant.Members,
                        participant.SeedRank,
                        participant.SeparationKey))
                .ToList(),
            new RandomizationPolicy(mode, submittedSeed),
            options is null
                ? null
                : new DrawOptions(
                    options.Knockout is { } knockout ? new KnockoutOptions(knockout.ThirdPlaceMatch) : null,
                    options.RoundRobin is { } roundRobin ? new RoundRobinOptions(roundRobin.Legs) : null,
                    options.Groups is { } groups
                        ? new GroupStageOptions(groups.GroupCount, groups.GroupSize, groups.QualifiersPerGroup)
                        : null));

        var scoring = options?.Scoring is { } scoringBody
            ? new ScoringRules(thirdSetSuperTiebreak: scoringBody.ThirdSetSuperTiebreak)
            : ScoringRules.Default;

        return Result.Success(new DrawSubmission(request, scoring, submittedSeed ?? GenerateSeed()));
    }

    /// <summary>
    /// Translates a match result body for <paramref name="matchId"/>. Unknown status or side names fail with
    /// <see cref="MatchResultErrors.InvalidStatus"/> / <see cref="MatchResultErrors.InvalidWinnerSide"/>, a null set
    /// with <see cref="MatchResultErrors.InvalidSet"/>; times are converted to UTC. Scores are validated by the domain.
    /// </summary>
    public static Result<MatchResult> ToMatchResult(string matchId, MatchResultBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!DrawWireNames.TryParse(body.Status, out MatchStatus status))
            return Result.Failure<MatchResult>(MatchResultErrors.InvalidStatus);

        MatchSide? winnerSide = null;
        if (body.WinnerSide is not null)
        {
            if (!DrawWireNames.TryParse(body.WinnerSide, out MatchSide side))
                return Result.Failure<MatchResult>(MatchResultErrors.InvalidWinnerSide);

            winnerSide = side;
        }

        var sets = body.Sets ?? [];
        if (sets.Any(set => set is null))
            return Result.Failure<MatchResult>(MatchResultErrors.InvalidSet);

        return Result.Success(new MatchResult(
            matchId,
            status,
            sets.Select(set => new SetScore(
                    set!.Home,
                    set.Away,
                    set.Tiebreak is { } tiebreak ? new TiebreakScore(tiebreak.Home, tiebreak.Away) : null))
                .ToList(),
            winnerSide,
            string.IsNullOrWhiteSpace(body.CourtName) ? null : body.CourtName.Trim(),
            body.ScheduledAt?.UtcDateTime,
            body.StartedAt?.UtcDateTime,
            body.FinishedAt?.UtcDateTime));
    }

    private static ulong GenerateSeed() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
}
