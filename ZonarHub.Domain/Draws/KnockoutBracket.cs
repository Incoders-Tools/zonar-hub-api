using System.Numerics;

namespace ZonarHub.Domain.Draws;

/// <summary>
/// Builds a knockout phase from entrants ordered by seed. Entrants are slots, so a bracket can be fed by known
/// participants (single elimination) or by group positions (groups then knockout).
/// </summary>
internal static class KnockoutBracket
{
    public const string PhaseId = "KO";

    /// <summary>
    /// The bracket size is the next power of two. Entrant <c>i</c> (0-based) takes seed <c>i + 1</c> in the standard
    /// seeding order; seeds above the entrant count are byes, so byes fall to the top seeds and never meet each other.
    /// A bye match resolves its next-round slot to the advancing entrant; every other match feeds a
    /// <see cref="WinnerOfSlot"/>. With <paramref name="thirdPlaceMatch"/> the final round gets a second match between
    /// the semifinal losers; callers ensure the bracket then has at least four entrants.
    /// </summary>
    public static Phase Build(string phaseId, IReadOnlyList<Slot> entrantsBySeed, bool thirdPlaceMatch)
    {
        var size = (int)BitOperations.RoundUpToPowerOf2((uint)entrantsBySeed.Count);
        var roundCount = BitOperations.Log2((uint)size);
        var rounds = new List<Round>(roundCount);

        var slots = StandardOrder(size)
            .Select(seed => seed <= entrantsBySeed.Count ? entrantsBySeed[seed - 1] : Slot.Bye)
            .ToArray();

        for (var roundNumber = 1; roundNumber <= roundCount; roundNumber++)
        {
            var isFinal = roundNumber == roundCount;
            var feedsThirdPlace = thirdPlaceMatch && roundNumber == roundCount - 1;
            var matches = new List<Match>();
            var nextSlots = new Slot[slots.Length / 2];

            for (var index = 0; index < nextSlots.Length; index++)
            {
                var id = MatchId(phaseId, roundNumber, index + 1);
                var match = new Match(
                    id,
                    roundNumber,
                    index + 1,
                    slots[2 * index],
                    slots[2 * index + 1],
                    isFinal ? null : MatchId(phaseId, roundNumber + 1, index / 2 + 1),
                    feedsThirdPlace ? MatchId(phaseId, roundCount, 2) : null);

                matches.Add(match);

                // The standard order always puts the better seed at home, so only the away side can be a bye.
                nextSlots[index] = match.Away is ByeSlot ? match.Home : new WinnerOfSlot(id);
            }

            if (isFinal && thirdPlaceMatch)
            {
                var semifinals = rounds[^1].Matches;
                matches.Add(new Match(
                    MatchId(phaseId, roundNumber, 2),
                    roundNumber,
                    2,
                    new LoserOfSlot(semifinals[0].Id),
                    new LoserOfSlot(semifinals[1].Id)));
            }

            rounds.Add(new Round(roundNumber, RoundName(nextSlots.Length), matches.AsReadOnly()));
            slots = nextSlots;
        }

        return new Phase(phaseId, PhaseKind.Knockout, rounds.AsReadOnly());
    }

    /// <summary>
    /// Seed number at each bracket position: 1 and 2 at opposite ends, 3 and 4 in the other quarters, and so on.
    /// Built by expanding each seed <c>x</c> of the half-size order into the pair <c>(x, size + 1 - x)</c>.
    /// </summary>
    private static IReadOnlyList<int> StandardOrder(int size)
    {
        IReadOnlyList<int> order = [1];
        while (order.Count < size)
        {
            var doubled = order.Count * 2;
            order = order.SelectMany(seed => new[] { seed, doubled + 1 - seed }).ToArray();
        }

        return order;
    }

    private static string MatchId(string phaseId, int roundNumber, int matchNumber) =>
        $"{phaseId}-R{roundNumber}-M{matchNumber}";

    private static string RoundName(int matchCount) => matchCount switch
    {
        1 => "final",
        2 => "semifinal",
        4 => "quarterfinal",
        _ => $"round_of_{matchCount * 2}",
    };
}
