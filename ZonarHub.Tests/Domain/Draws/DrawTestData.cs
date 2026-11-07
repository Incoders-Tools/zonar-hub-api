using ZonarHub.Domain.Draws;

namespace ZonarHub.Tests.Domain.Draws;

/// <summary>
/// Builders and projections shared by the draw engine tests.
/// </summary>
internal static class DrawTestData
{
    public static IReadOnlyList<DrawParticipant> Participants(int count, Func<int, int?>? seedRank = null) =>
        Enumerable.Range(1, count)
            .Select(i => new DrawParticipant($"p{i}", $"Player {i}", seedRank: seedRank?.Invoke(i)))
            .ToList();

    public static DrawRequest SingleElimination(
        IReadOnlyList<DrawParticipant> participants,
        RandomizationPolicy? randomization = null,
        bool thirdPlaceMatch = false,
        ParticipationFormat participationFormat = ParticipationFormat.Individual) =>
        new(
            DrawFormat.SingleElimination,
            participationFormat,
            participants,
            randomization ?? RandomizationPolicy.Seeded(2026),
            new DrawOptions(Knockout: new KnockoutOptions(thirdPlaceMatch)));

    public static DrawRequest RoundRobin(
        IReadOnlyList<DrawParticipant> participants,
        RandomizationPolicy? randomization = null,
        int legs = 1) =>
        new(
            DrawFormat.RoundRobin,
            ParticipationFormat.Individual,
            participants,
            randomization ?? RandomizationPolicy.Seeded(2026),
            new DrawOptions(RoundRobin: new RoundRobinOptions(legs)));

    public static DrawRequest Groups(
        IReadOnlyList<DrawParticipant> participants,
        RandomizationPolicy? randomization = null,
        int? groupCount = null,
        int? groupSize = null,
        int qualifiers = 2,
        bool thirdPlaceMatch = false,
        int legs = 1) =>
        new(
            DrawFormat.GroupsThenKnockout,
            ParticipationFormat.Individual,
            participants,
            randomization ?? RandomizationPolicy.Seeded(2026),
            new DrawOptions(
                new KnockoutOptions(thirdPlaceMatch),
                new RoundRobinOptions(legs),
                new GroupStageOptions(groupCount, groupSize, qualifiers)));

    public static Draw Generate(DrawRequest request)
    {
        var result = DrawGenerator.Generate(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : string.Empty);
        return result.Value;
    }

    public static Phase Knockout(Draw draw) => Assert.Single(draw.Phases, phase => phase.Kind == PhaseKind.Knockout);

    public static Phase RoundRobinPhase(Draw draw) => Assert.Single(draw.Phases, phase => phase.Kind == PhaseKind.RoundRobin);

    public static Phase GroupStage(Draw draw) => Assert.Single(draw.Phases, phase => phase.Kind == PhaseKind.GroupStage);

    public static IEnumerable<string> ParticipantIds(Match match) =>
        new[] { match.Home, match.Away }.OfType<ParticipantSlot>().Select(slot => slot.ParticipantId);

    /// <summary>Zero-based index of the first-round match holding the participant.</summary>
    public static int FirstRoundMatchIndex(Phase knockout, string participantId) =>
        knockout.Rounds[0].Matches.ToList().FindIndex(match => ParticipantIds(match).Contains(participantId));

    /// <summary>Ids of the participants that received a first-round bye.</summary>
    public static IReadOnlySet<string> ByeRecipients(Phase knockout) =>
        knockout.Rounds[0].Matches.Where(match => match.IsBye).SelectMany(ParticipantIds).ToHashSet();

    /// <summary>Flattens a draw to value-comparable rows so two draws can be compared deeply.</summary>
    public static IReadOnlyList<object> Snapshot(Draw draw) =>
        draw.Phases
            .SelectMany(phase => phase.Rounds.SelectMany(round =>
                round.Matches.Select(match => (object)(phase.Id, phase.Kind, round.Number, round.Name, string.Join(',', round.RestingParticipantIds), match)))
                .Concat(phase.Groups.Select(group => (object)(phase.Id, group.Id, string.Join(',', group.ParticipantIds)))))
            .ToList();
}
