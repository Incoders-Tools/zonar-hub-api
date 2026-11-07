namespace ZonarHub.Domain.Draws;

/// <summary>
/// Order in which participants enter a format: bracket seeds for a knockout, initial circle positions for a round robin.
/// </summary>
internal static class SeedOrder
{
    /// <summary>
    /// Random mode shuffles everyone. Seeded mode puts ranked participants first, by ascending rank, followed by the
    /// unranked ones shuffled.
    /// </summary>
    public static List<DrawParticipant> Arrange(
        IReadOnlyList<DrawParticipant> participants,
        RandomizationMode mode,
        DeterministicRandom random)
    {
        if (mode == RandomizationMode.Random)
        {
            var everyone = participants.ToList();
            random.Shuffle(everyone);
            return everyone;
        }

        var seeded = participants.Where(p => p.SeedRank is not null).OrderBy(p => p.SeedRank).ToList();
        var unseeded = participants.Where(p => p.SeedRank is null).ToList();
        random.Shuffle(unseeded);
        return [.. seeded, .. unseeded];
    }
}
