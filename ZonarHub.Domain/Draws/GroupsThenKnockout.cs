using System.Numerics;
using System.Text;
using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Draws;

/// <summary>
/// Groups then knockout: a <see cref="PhaseKind.GroupStage"/> phase (id <c>G</c>) of round-robin groups, followed by a
/// knockout phase (id <c>KO</c>) fed by <see cref="GroupPositionSlot"/>s. Separation keys spread participants of one
/// key over different groups when possible (<see cref="Separation.SeparateGroups"/>); the knockout entrants are group
/// positions, so keys do not apply to it.
/// </summary>
internal static class GroupsThenKnockout
{
    public const string PhaseId = "G";

    /// <summary>Target group size used when neither a group count nor a group size is given.</summary>
    public const int DefaultGroupSize = 4;

    private const int MinGroupSize = 2;
    private const int MinLegs = 1;
    private const int MaxLegs = 2;
    private const int MinKnockoutEntrants = 2;
    private const int ThirdPlaceMinEntrants = 4;

    public static Result<IReadOnlyList<Phase>> Build(DrawRequest request, DeterministicRandom random)
    {
        var options = request.Options;
        var legs = options?.RoundRobin?.Legs ?? MinLegs;
        if (legs is < MinLegs or > MaxLegs)
            return Result.Failure<IReadOnlyList<Phase>>(DrawErrors.InvalidRoundRobinLegs);

        var groupOptions = options?.Groups ?? new GroupStageOptions();
        var sizes = GroupSizes(request.Participants.Count, groupOptions);
        if (sizes.IsFailure)
            return Result.Failure<IReadOnlyList<Phase>>(sizes.Error);

        var qualifiers = groupOptions.QualifiersPerGroup;
        if (qualifiers < 1 || qualifiers >= sizes.Value[^1])
            return Result.Failure<IReadOnlyList<Phase>>(DrawErrors.InvalidQualifiers);

        var entrantCount = sizes.Value.Count * qualifiers;
        if (entrantCount < MinKnockoutEntrants)
            return Result.Failure<IReadOnlyList<Phase>>(DrawErrors.KnockoutMinQualifiers);

        var thirdPlaceMatch = options?.Knockout?.ThirdPlaceMatch ?? false;
        if (thirdPlaceMatch && entrantCount < ThirdPlaceMinEntrants)
            return Result.Failure<IReadOnlyList<Phase>>(DrawErrors.ThirdPlaceMinParticipants);

        var order = SeedOrder.Arrange(request.Participants, request.Randomization.Mode, random);
        var distribution = Distribute(order, sizes.Value);
        Separation.SeparateGroups(distribution, request.Randomization.Mode);

        var groups = distribution
            .Select((participants, index) =>
            {
                var id = GroupId(index);
                var members = participants.Select(participant => participant.Id).ToList();
                return new Group(id, members.AsReadOnly(), RoundRobinSchedule.Build($"{PhaseId}-{id}", members, legs));
            })
            .ToList();

        var groupStage = new Phase(PhaseId, PhaseKind.GroupStage, MergeRounds(groups)) { Groups = groups.AsReadOnly() };
        var knockout = KnockoutBracket.Build(
            KnockoutBracket.PhaseId,
            KnockoutEntrants(groups.Select(group => group.Id).ToList(), qualifiers),
            thirdPlaceMatch);

        return Result.Success<IReadOnlyList<Phase>>(new[] { groupStage, knockout }.AsReadOnly());
    }

    /// <summary>
    /// Group sizes, largest first, differing by at most one. A group count is used as given; a target size gives
    /// <c>ceil(count / size)</c> groups, reduced to <c>floor(count / 2)</c> when needed so no group falls below two
    /// (so a group may then exceed the target). Without either, the target size is <see cref="DefaultGroupSize"/>.
    /// </summary>
    private static Result<IReadOnlyList<int>> GroupSizes(int participantCount, GroupStageOptions options)
    {
        if (options.GroupCount is not null && options.GroupSize is not null)
            return Result.Failure<IReadOnlyList<int>>(DrawErrors.GroupOptionsConflict);

        int groupCount;
        if (options.GroupCount is int requestedCount)
        {
            if (requestedCount < 1)
                return Result.Failure<IReadOnlyList<int>>(DrawErrors.InvalidGroupCount);

            if (requestedCount > participantCount / MinGroupSize)
                return Result.Failure<IReadOnlyList<int>>(DrawErrors.GroupTooSmall);

            groupCount = requestedCount;
        }
        else
        {
            var targetSize = options.GroupSize ?? DefaultGroupSize;
            if (targetSize < MinGroupSize)
                return Result.Failure<IReadOnlyList<int>>(DrawErrors.InvalidGroupSize);

            groupCount = Math.Min((participantCount + targetSize - 1) / targetSize, participantCount / MinGroupSize);
        }

        var baseSize = participantCount / groupCount;
        var larger = participantCount % groupCount;
        return Result.Success<IReadOnlyList<int>>(
            Enumerable.Range(0, groupCount).Select(index => index < larger ? baseSize + 1 : baseSize).ToArray());
    }

    /// <summary>
    /// Snake distribution: forward over the groups (A, B, C), then backward (C, B, A), and so on, skipping groups that
    /// already hold their size. Seeds 1 to the group count therefore head distinct groups, and when the last pass runs
    /// backward its participants still go to the first (larger) groups.
    /// </summary>
    private static List<List<DrawParticipant>> Distribute(IReadOnlyList<DrawParticipant> order, IReadOnlyList<int> sizes)
    {
        var groups = sizes.Select(size => new List<DrawParticipant>(size)).ToList();
        var next = 0;
        for (var pass = 0; next < order.Count; pass++)
        {
            for (var step = 0; step < groups.Count && next < order.Count; step++)
            {
                var index = pass % 2 == 0 ? step : groups.Count - 1 - step;
                if (groups[index].Count < sizes[index])
                    groups[index].Add(order[next++]);
            }
        }

        return groups;
    }

    /// <summary>Spreadsheet column order: <c>A</c>..<c>Z</c>, <c>AA</c>..<c>AZ</c>, <c>BA</c>, ...</summary>
    private static string GroupId(int index)
    {
        var id = new StringBuilder();
        for (var value = index + 1; value > 0; value = (value - 1) / 26)
            id.Insert(0, (char)('A' + (value - 1) % 26));

        return id.ToString();
    }

    /// <summary>Matchday <c>N</c> across every group: each group's round <c>N</c>, in group order.</summary>
    private static IReadOnlyList<Round> MergeRounds(IReadOnlyList<Group> groups)
    {
        var roundCount = groups.Max(group => group.Rounds.Count);
        return Enumerable.Range(0, roundCount)
            .Select(index =>
            {
                var groupRounds = groups.Where(group => group.Rounds.Count > index).Select(group => group.Rounds[index]).ToList();
                return new Round(
                    index + 1,
                    RoundRobinSchedule.RoundName,
                    groupRounds.SelectMany(round => round.Matches).ToList().AsReadOnly())
                {
                    RestingParticipantIds = groupRounds.SelectMany(round => round.RestingParticipantIds).ToList().AsReadOnly(),
                };
            })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Knockout entrants by seed, in tiers of one position per group: every group winner (by group order), then every
    /// runner-up, and so on, so byes fall to the best-placed qualifiers. Winners keep the group order; each later tier
    /// is reordered, when needed, so that no first-round match pairs two qualifiers of the same group.
    /// </summary>
    /// <remarks>
    /// Entrants of one tier always come from distinct groups, so only matches across tiers can clash. Tiers are placed
    /// top-down: a seed whose first-round opponent belongs to an earlier tier must not take that opponent's group. Each
    /// seed then forbids at most one group, and the forbidden seeds of a tier face a contiguous run of earlier seeds,
    /// which never repeats a group for three or more groups and aligns with a whole tier for two; so with two or more
    /// groups a clash-free order always exists. It is found by augmenting-path matching, each seed trying groups from
    /// its own position onward (cyclically), which keeps the plain group order when nothing clashes. A single group
    /// cannot avoid clashes and keeps the plain order.
    /// </remarks>
    private static IReadOnlyList<Slot> KnockoutEntrants(IReadOnlyList<string> groupIds, int qualifiers)
    {
        var groupCount = groupIds.Count;
        var entrantCount = groupCount * qualifiers;
        var bracketSize = (int)BitOperations.RoundUpToPowerOf2((uint)entrantCount);

        // Group index of the entrant at each 0-based seed.
        var groupOfSeed = new int[entrantCount];
        for (var seed = 0; seed < groupCount; seed++)
            groupOfSeed[seed] = seed;

        for (var tier = 1; tier < qualifiers; tier++)
        {
            var first = tier * groupCount;
            var forbidden = new int[groupCount];
            for (var position = 0; position < groupCount; position++)
            {
                // Standard seeding pairs seed s with seed (bracketSize + 1 - s), 1-based.
                var opponent = bracketSize - 1 - (first + position);
                forbidden[position] = opponent < first ? groupOfSeed[opponent] : -1;
            }

            var assignment = AssignGroups(groupCount, forbidden)
                ?? Enumerable.Range(0, groupCount).ToArray();
            Array.Copy(assignment, 0, groupOfSeed, first, groupCount);
        }

        return groupOfSeed
            .Select((group, seed) => (Slot)new GroupPositionSlot(groupIds[group], seed / groupCount + 1))
            .ToList();
    }

    /// <summary>
    /// Perfect matching of tier positions to groups avoiding each position's forbidden group (<c>-1</c> for none), or
    /// <c>null</c> when none exists.
    /// </summary>
    private static int[]? AssignGroups(int groupCount, IReadOnlyList<int> forbidden)
    {
        var ownerOfGroup = Enumerable.Repeat(-1, groupCount).ToArray();

        for (var position = 0; position < groupCount; position++)
        {
            if (!TryAssign(position, new bool[groupCount]))
                return null;
        }

        var groupOfPosition = new int[groupCount];
        for (var group = 0; group < groupCount; group++)
            groupOfPosition[ownerOfGroup[group]] = group;

        return groupOfPosition;

        bool TryAssign(int position, bool[] visited)
        {
            for (var offset = 0; offset < groupCount; offset++)
            {
                var group = (position + offset) % groupCount;
                if (group == forbidden[position] || visited[group])
                    continue;

                visited[group] = true;
                if (ownerOfGroup[group] == -1 || TryAssign(ownerOfGroup[group], visited))
                {
                    ownerOfGroup[group] = position;
                    return true;
                }
            }

            return false;
        }
    }
}
