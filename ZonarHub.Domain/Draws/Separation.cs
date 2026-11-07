namespace ZonarHub.Domain.Draws;

/// <summary>
/// Conditioned randomization (separation keys): after the seeded placement, moves movable participants so that those
/// sharing a <see cref="DrawParticipant.SeparationKey"/> avoid each other, then reports what could not be avoided.
/// </summary>
/// <remarks>
/// Keys are trimmed and compared case-insensitively (ordinal); blank keys are no key. The repair uses no randomness: it
/// is a deterministic function of the seeded placement, applying only swaps of two movable participants that strictly
/// reduce the number of clashes, so it always ends and leaves a placement without clashes untouched (no keys, or no
/// shared keys, keep the draw identical to one without separation). Movable participants are the unranked ones in
/// <see cref="RandomizationMode.Seeded"/> mode and everyone in <see cref="RandomizationMode.Random"/> mode, so ranked
/// participants keep their seed positions and knockout byes stay on the same seed positions. A round robin is never
/// changed (everyone meets everyone), and a group stage's knockout is fed by group positions, so keys only apply to
/// knockout first rounds over known participants and to group membership. Keeping same-key participants in opposite
/// halves or quarters beyond the first round is not attempted.
/// </remarks>
internal static class Separation
{
    public static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>The participant's trimmed key, or <c>null</c> when it has none.</summary>
    public static string? Key(DrawParticipant participant) =>
        string.IsNullOrWhiteSpace(participant.SeparationKey) ? null : participant.SeparationKey.Trim();

    public static bool IsMovable(DrawParticipant participant, RandomizationMode mode) =>
        mode == RandomizationMode.Random || participant.SeedRank is null;

    /// <summary>
    /// Reorders <paramref name="entrantsBySeed"/> (as fed to <see cref="KnockoutBracket.Build"/>) so that no first-round
    /// match pairs two participants of the same key whenever an arrangement of the movable participants allows it.
    /// </summary>
    /// <remarks>
    /// Seed <c>i</c> (0-based) meets seed <c>bracketSize - 1 - i</c>, or has a bye when that seed does not exist. While a
    /// match clashes on key <c>k</c>, one of its movable participants is swapped with a movable participant elsewhere
    /// when that strictly reduces the clashes of the two matches involved. When no such swap exists, every other match holds
    /// a <c>k</c> (or faces one) and every bye goes to a <c>k</c>, so key <c>k</c> has more movable participants than
    /// clash-free places: the clash is unavoidable. Candidates are scanned from the lowest seed up, so the top seeds
    /// move last.
    /// </remarks>
    public static void SeparateFirstRound(List<DrawParticipant> entrantsBySeed, int bracketSize, RandomizationMode mode)
    {
        var count = entrantsBySeed.Count;
        int Opponent(int seed) => bracketSize - 1 - seed < count ? bracketSize - 1 - seed : -1;

        bool Clashes(int seed)
        {
            var opponent = Opponent(seed);
            return opponent >= 0 && SameKey(entrantsBySeed[seed], entrantsBySeed[opponent]);
        }

        bool changed;
        do
        {
            changed = false;
            for (var seed = count - 1; seed >= 0; seed--)
            {
                if (!IsMovable(entrantsBySeed[seed], mode) || !Clashes(seed))
                    continue;

                for (var other = count - 1; other >= 0; other--)
                {
                    if (other == seed || other == Opponent(seed) || !IsMovable(entrantsBySeed[other], mode))
                        continue;

                    var before = (Clashes(seed) ? 1 : 0) + (Clashes(other) ? 1 : 0);
                    Swap(entrantsBySeed, seed, other);
                    if ((Clashes(seed) ? 1 : 0) + (Clashes(other) ? 1 : 0) < before)
                    {
                        changed = true;
                        break;
                    }

                    Swap(entrantsBySeed, seed, other);
                }
            }
        }
        while (changed);
    }

    /// <summary>
    /// Swaps movable participants between <paramref name="groups"/> (sizes unchanged) while a swap strictly reduces the
    /// number of same-key pairs inside groups.
    /// </summary>
    /// <remarks>
    /// The result is a local optimum: no single swap of two movable participants reduces the same-key pairs further.
    /// A single shared key therefore ends spread as evenly as the movable places allow; with several keys competing
    /// for the same groups it is a best effort, not a guaranteed global minimum. Candidates are scanned from the last
    /// group member backward, so group heads move last.
    /// </remarks>
    public static void SeparateGroups(List<List<DrawParticipant>> groups, RandomizationMode mode)
    {
        var places = groups
            .SelectMany((members, group) => members.Select((_, index) => (Group: group, Index: index)))
            .ToList();

        int Pairs(int group)
        {
            var keyed = groups[group].Select(Key).Where(key => key is not null).ToList();
            return keyed.GroupBy(key => key, KeyComparer).Sum(members => members.Count() * (members.Count() - 1) / 2);
        }

        bool changed;
        do
        {
            changed = false;
            for (var place = places.Count - 1; place >= 0; place--)
            {
                var (group, index) = places[place];
                var participant = groups[group][index];
                if (!IsMovable(participant, mode) || Key(participant) is null
                    || !groups[group].Where((_, member) => member != index).Any(member => SameKey(member, participant)))
                    continue;

                for (var otherPlace = places.Count - 1; otherPlace >= 0; otherPlace--)
                {
                    var (otherGroup, otherIndex) = places[otherPlace];
                    if (otherGroup == group || !IsMovable(groups[otherGroup][otherIndex], mode))
                        continue;

                    var before = Pairs(group) + Pairs(otherGroup);
                    (groups[group][index], groups[otherGroup][otherIndex]) = (groups[otherGroup][otherIndex], groups[group][index]);
                    if (Pairs(group) + Pairs(otherGroup) < before)
                    {
                        changed = true;
                        break;
                    }

                    (groups[group][index], groups[otherGroup][otherIndex]) = (groups[otherGroup][otherIndex], groups[group][index]);
                }
            }
        }
        while (changed);
    }

    /// <summary>
    /// The separations a generated draw could not satisfy: each first-round knockout match between two known
    /// participants of one key, then each group holding two or more participants of one key (keys in order of first
    /// appearance in the group). Ordered by phase, then match or group.
    /// </summary>
    public static IReadOnlyList<SeparationConflict> FindConflicts(IReadOnlyList<DrawParticipant> participants, IReadOnlyList<Phase> phases)
    {
        var byId = participants.ToDictionary(participant => participant.Id, StringComparer.Ordinal);
        var conflicts = new List<SeparationConflict>();

        foreach (var phase in phases)
        {
            if (phase.Kind == PhaseKind.Knockout)
            {
                foreach (var match in phase.Rounds[0].Matches)
                {
                    if (match is { Home: ParticipantSlot home, Away: ParticipantSlot away }
                        && SameKey(byId[home.ParticipantId], byId[away.ParticipantId]))
                    {
                        conflicts.Add(new SeparationConflict(
                            Key(byId[home.ParticipantId])!,
                            new[] { home.ParticipantId, away.ParticipantId }.AsReadOnly(),
                            SeparationConflictKind.FirstRoundMatch,
                            match.Id));
                    }
                }
            }

            foreach (var group in phase.Groups)
            {
                var sharedKeys = group.ParticipantIds
                    .Where(id => Key(byId[id]) is not null)
                    .GroupBy(id => Key(byId[id]), KeyComparer)
                    .Where(members => members.Count() > 1);

                conflicts.AddRange(sharedKeys.Select(members => new SeparationConflict(
                    Key(byId[members.First()])!,
                    members.ToList().AsReadOnly(),
                    SeparationConflictKind.SameGroup,
                    group.Id)));
            }
        }

        return conflicts.AsReadOnly();
    }

    private static bool SameKey(DrawParticipant first, DrawParticipant second)
    {
        var key = Key(first);
        return key is not null && KeyComparer.Equals(key, Key(second));
    }

    private static void Swap(List<DrawParticipant> items, int first, int second) =>
        (items[first], items[second]) = (items[second], items[first]);
}
