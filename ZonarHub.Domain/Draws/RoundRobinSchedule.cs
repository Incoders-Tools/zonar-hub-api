namespace ZonarHub.Domain.Draws;

/// <summary>
/// Round-robin rounds by the circle method, reusable for a whole draw (<c>RR</c>) or for one group (<c>G-A</c>).
/// </summary>
internal static class RoundRobinSchedule
{
    public const string RoundName = "matchday";

    /// <summary>
    /// Schedules every pair of <paramref name="participantIds"/> once per leg; callers validate at least two
    /// participants and one or two legs. Match ids are <c>{idPrefix}-R{round}-M{number}</c>.
    /// </summary>
    /// <remarks>
    /// The circle has an even number of positions: the participants in the given order, preceded by an empty rest
    /// position when the count is odd. Position 0 stays fixed and the rest rotate one step per round; in each round
    /// position <c>i</c> meets position <c>size - 1 - i</c>. A participant facing the rest position sits out that
    /// round: it is listed in <see cref="Round.RestingParticipantIds"/> and gets no match, so each participant rests
    /// exactly once per leg.
    /// <para>
    /// Home/away: position <c>i</c> of the first half is home against position <c>size - 1 - i</c>, except the fixed
    /// position, which alternates by round. Every rotating participant visits each non-fixed position once per leg, so
    /// its home and away counts differ by at most one. A second leg repeats the first with home and away swapped, as
    /// rounds numbered after the first leg.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Round> Build(string idPrefix, IReadOnlyList<string> participantIds, int legs)
    {
        var circle = new List<string?>(participantIds.Count + 1);
        if (participantIds.Count % 2 == 1)
            circle.Add(null);
        circle.AddRange(participantIds);

        var size = circle.Count;
        var roundsPerLeg = size - 1;
        var firstLeg = new List<(List<(string Home, string Away)> Pairs, string[] Resting)>(roundsPerLeg);

        for (var round = 0; round < roundsPerLeg; round++)
        {
            var pairs = new List<(string Home, string Away)>(size / 2);
            var resting = new List<string>(1);

            for (var i = 0; i < size / 2; i++)
            {
                var first = circle[i];
                var second = circle[size - 1 - i]!;

                // Only the fixed position 0 can be the empty rest position.
                if (first is null)
                {
                    resting.Add(second);
                    continue;
                }

                var firstAtHome = i != 0 || round % 2 == 0;
                pairs.Add(firstAtHome ? (first, second) : (second, first));
            }

            firstLeg.Add((pairs, resting.ToArray()));

            var last = circle[size - 1];
            circle.RemoveAt(size - 1);
            circle.Insert(1, last);
        }

        var rounds = new List<Round>(roundsPerLeg * legs);
        for (var leg = 0; leg < legs; leg++)
        {
            var mirrored = leg == 1;
            foreach (var (pairs, resting) in firstLeg)
            {
                var number = rounds.Count + 1;
                var matches = pairs
                    .Select((pair, index) => new Match(
                        $"{idPrefix}-R{number}-M{index + 1}",
                        number,
                        index + 1,
                        new ParticipantSlot(mirrored ? pair.Away : pair.Home),
                        new ParticipantSlot(mirrored ? pair.Home : pair.Away)))
                    .ToList();

                rounds.Add(new Round(number, RoundName, matches.AsReadOnly())
                {
                    RestingParticipantIds = Array.AsReadOnly(resting),
                });
            }
        }

        return rounds.AsReadOnly();
    }
}
