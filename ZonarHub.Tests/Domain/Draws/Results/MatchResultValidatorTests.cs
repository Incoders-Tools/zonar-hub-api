using ZonarHub.Domain.Common;
using ZonarHub.Domain.Draws.Results;
using static ZonarHub.Tests.Domain.Draws.Results.ResultTestData;

namespace ZonarHub.Tests.Domain.Draws.Results;

public class MatchResultValidatorTests
{
    private static readonly DateTime Start = new(2026, 11, 7, 18, 0, 0, DateTimeKind.Utc);

    public static TheoryData<string, MatchResult> ValidResults => new()
    {
        { "scheduled without score", new MatchResult("M", MatchStatus.Scheduled, courtName: "Court 1", scheduledAtUtc: Start) },
        { "6-4 6-3", Completed(MatchSide.Home, S(6, 4), S(6, 3)) },
        { "6-0 6-1 away", Completed(MatchSide.Away, S(0, 6), S(1, 6)) },
        { "7-5 6-7(5) 10-8", Completed(MatchSide.Home, S(7, 5), S(6, 7, 5, 7), S(10, 8)) },
        { "7-6(10-8) 6-0", Completed(MatchSide.Home, S(7, 6, 10, 8), S(6, 0)) },
        { "7-6 without tiebreak points", Completed(MatchSide.Home, S(7, 6), S(6, 2)) },
        { "4-6 6-4 11-13 super tie-break", Completed(MatchSide.Away, S(4, 6), S(6, 4), S(11, 13)) },
        { "super tie-break 10-0", Completed(MatchSide.Home, S(6, 1), S(1, 6), S(10, 0)) },
        { "walkover", new MatchResult("M", MatchStatus.Walkover, winnerSide: MatchSide.Away) },
        { "retired 6-3 2-1", new MatchResult("M", MatchStatus.Retired, [S(6, 3), S(2, 1)], MatchSide.Away) },
        { "retired before any game", new MatchResult("M", MatchStatus.Retired, winnerSide: MatchSide.Home) },
        { "in progress, no games yet", new MatchResult("M", MatchStatus.InProgress, startedAtUtc: Start) },
        { "in progress 6-4 5-5", new MatchResult("M", MatchStatus.InProgress, [S(6, 4), S(5, 5)]) },
        { "in progress at the tie-break", new MatchResult("M", MatchStatus.InProgress, [S(6, 6)]) },
        { "in progress 6-4 4-6 9-9", new MatchResult("M", MatchStatus.InProgress, [S(6, 4), S(4, 6), S(9, 9)]) },
        { "in progress super tie-break 10-9", new MatchResult("M", MatchStatus.InProgress, [S(6, 4), S(4, 6), S(10, 9)]) },
        { "in progress super tie-break 11-11", new MatchResult("M", MatchStatus.InProgress, [S(6, 4), S(4, 6), S(11, 11)]) },
        { "finished at the start time", Completed(MatchSide.Home, S(6, 4), S(6, 3)) with { StartedAtUtc = Start, FinishedAtUtc = Start } },
        { "finished after start", Completed(MatchSide.Home, S(6, 4), S(6, 3)) with { StartedAtUtc = Start, FinishedAtUtc = Start.AddMinutes(95) } },
        { "only finished time", Completed(MatchSide.Home, S(6, 4), S(6, 3)) with { FinishedAtUtc = Start } },
    };

    public static TheoryData<string, MatchResult, string> InvalidResults => new()
    {
        { "6-5 is not a finished set", Completed(MatchSide.Home, S(6, 5), S(6, 0)), "draws.results.invalid_set" },
        { "8-6", Completed(MatchSide.Home, S(8, 6), S(6, 0)), "draws.results.invalid_set" },
        { "7-4", Completed(MatchSide.Home, S(7, 4), S(6, 0)), "draws.results.invalid_set" },
        { "7-7", new MatchResult("M", MatchStatus.InProgress, [S(7, 7)]), "draws.results.invalid_set" },
        { "negative games", new MatchResult("M", MatchStatus.InProgress, [S(-1, 0)]), "draws.results.invalid_set" },
        { "missing set", new MatchResult("M", MatchStatus.InProgress, [null!]), "draws.results.invalid_set" },
        { "tiebreak on 6-4", Completed(MatchSide.Home, S(6, 4, 7, 5), S(6, 0)), "draws.results.invalid_set" },
        { "tiebreak won by the set loser", Completed(MatchSide.Home, S(7, 6, 5, 7), S(6, 0)), "draws.results.invalid_set" },
        { "tiebreak 6-4", Completed(MatchSide.Home, S(7, 6, 6, 4), S(6, 0)), "draws.results.invalid_set" },
        { "tiebreak 7-6", Completed(MatchSide.Home, S(7, 6, 7, 6), S(6, 0)), "draws.results.invalid_set" },
        { "tiebreak 10-7", Completed(MatchSide.Home, S(7, 6, 10, 7), S(6, 0)), "draws.results.invalid_set" },
        { "negative tiebreak", Completed(MatchSide.Home, S(7, 6, 7, -1), S(6, 0)), "draws.results.invalid_set" },
        { "tiebreak on a partial set", new MatchResult("M", MatchStatus.InProgress, [S(6, 6, 3, 2)]), "draws.results.invalid_set" },
        { "partial set before the last one", new MatchResult("M", MatchStatus.InProgress, [S(5, 5), S(1, 0)]), "draws.results.invalid_set" },
        { "super tie-break 10-9", Completed(MatchSide.Home, S(6, 4), S(4, 6), S(10, 9)), "draws.results.invalid_super_tiebreak" },
        { "super tie-break 12-9", Completed(MatchSide.Home, S(6, 4), S(4, 6), S(12, 9)), "draws.results.invalid_super_tiebreak" },
        { "super tie-break 9-7", Completed(MatchSide.Home, S(6, 4), S(4, 6), S(9, 7)), "draws.results.invalid_super_tiebreak" },
        { "super tie-break with tiebreak points", Completed(MatchSide.Home, S(6, 4), S(4, 6), S(10, 8, 7, 5)), "draws.results.invalid_super_tiebreak" },
        { "super tie-break negative", new MatchResult("M", MatchStatus.InProgress, [S(6, 4), S(4, 6), S(-1, 3)]), "draws.results.invalid_super_tiebreak" },
        { "super tie-break in progress 11-8", new MatchResult("M", MatchStatus.InProgress, [S(6, 4), S(4, 6), S(11, 8)]), "draws.results.invalid_super_tiebreak" },
        { "four sets", Completed(MatchSide.Home, S(6, 4), S(4, 6), S(6, 4), S(6, 4)), "draws.results.too_many_sets" },
        { "a set after the match is decided", Completed(MatchSide.Home, S(6, 4), S(6, 4), S(6, 4)), "draws.results.too_many_sets" },
        { "one set only", Completed(MatchSide.Home, S(6, 4)), "draws.results.incomplete_score" },
        { "one set each", Completed(MatchSide.Home, S(6, 4), S(3, 6)), "draws.results.incomplete_score" },
        { "completed without sets", Completed(MatchSide.Home), "draws.results.incomplete_score" },
        { "2-0 with the wrong winner", Completed(MatchSide.Away, S(6, 4), S(6, 3)), "draws.results.winner_mismatch" },
        { "2-1 with the wrong winner", Completed(MatchSide.Home, S(6, 4), S(3, 6), S(8, 10)), "draws.results.winner_mismatch" },
        { "completed without winner", new MatchResult("M", MatchStatus.Completed, [S(6, 4), S(6, 3)]), "draws.results.winner_required" },
        { "walkover without winner", new MatchResult("M", MatchStatus.Walkover), "draws.results.winner_required" },
        { "retired without winner", new MatchResult("M", MatchStatus.Retired, [S(6, 3)]), "draws.results.winner_required" },
        { "walkover with sets", new MatchResult("M", MatchStatus.Walkover, [S(6, 0)], MatchSide.Home), "draws.results.sets_not_allowed_for_walkover" },
        { "scheduled with sets", new MatchResult("M", MatchStatus.Scheduled, [S(1, 0)]), "draws.results.sets_not_allowed_for_scheduled" },
        { "scheduled with winner", new MatchResult("M", MatchStatus.Scheduled, winnerSide: MatchSide.Home), "draws.results.winner_not_allowed" },
        { "in progress with winner", new MatchResult("M", MatchStatus.InProgress, [S(3, 1)], MatchSide.Home), "draws.results.winner_not_allowed" },
        { "in progress but already decided", new MatchResult("M", MatchStatus.InProgress, [S(6, 4), S(6, 3)]), "draws.results.score_decided" },
        { "retired with a decided score", new MatchResult("M", MatchStatus.Retired, [S(6, 4), S(6, 3)], MatchSide.Away), "draws.results.score_decided" },
        { "retired with an invalid set", new MatchResult("M", MatchStatus.Retired, [S(8, 3)], MatchSide.Away), "draws.results.invalid_set" },
        { "finished before started", Completed(MatchSide.Home, S(6, 4), S(6, 3)) with { StartedAtUtc = Start, FinishedAtUtc = Start.AddSeconds(-1) }, "draws.results.invalid_times" },
        { "unknown status", new MatchResult("M", (MatchStatus)99), "draws.results.invalid_status" },
        { "unknown winner side", new MatchResult("M", MatchStatus.Walkover, winnerSide: (MatchSide)7), "draws.results.invalid_winner_side" },
    };

    [Theory]
    [MemberData(nameof(ValidResults))]
    public void Validate_AcceptsValidResults(string scenario, MatchResult result)
    {
        var validation = MatchResultValidator.Validate(result, ScoringRules.Default);

        Assert.True(validation.IsSuccess, $"{scenario}: {(validation.IsFailure ? validation.Error.Code : string.Empty)}");
    }

    [Theory]
    [MemberData(nameof(InvalidResults))]
    public void Validate_RejectsInvalidResultsWithAStableCode(string scenario, MatchResult result, string code)
    {
        var validation = MatchResultValidator.Validate(result, ScoringRules.Default);

        Assert.True(validation.IsFailure, scenario);
        Assert.Equal(code, validation.Error.Code);
        Assert.Equal(ErrorType.Validation, validation.Error.Type);
        Assert.Equal(code.Replace("draws.results.", "draws.errors.results."), validation.Error.MessageKey);
    }

    [Fact]
    public void Validate_WithoutSuperTiebreak_PlaysTheThirdSetAsARegularSet()
    {
        var rules = new ScoringRules(thirdSetSuperTiebreak: false);

        Assert.True(MatchResultValidator.Validate(Completed(MatchSide.Home, S(6, 4), S(4, 6), S(7, 5)), rules).IsSuccess);
        Assert.Equal(
            "draws.results.invalid_set",
            MatchResultValidator.Validate(Completed(MatchSide.Home, S(6, 4), S(4, 6), S(10, 8)), rules).Error.Code);
    }

    [Fact]
    public void Validate_WithSuperTiebreak_RejectsARegularThirdSet()
    {
        Assert.Equal(
            "draws.results.invalid_super_tiebreak",
            MatchResultValidator.Validate(Completed(MatchSide.Home, S(6, 4), S(4, 6), S(7, 5)), ScoringRules.Default).Error.Code);
    }

    [Fact]
    public void Validate_BestOfFive_NeedsThreeSetsAndPlaysTheFifthAsSuperTiebreak()
    {
        var rules = new ScoringRules(bestOf: 5);

        Assert.True(MatchResultValidator.Validate(Completed(MatchSide.Home, S(6, 4), S(6, 3), S(7, 5)), rules).IsSuccess);
        Assert.True(MatchResultValidator.Validate(
            Completed(MatchSide.Away, S(6, 4), S(3, 6), S(6, 4), S(4, 6), S(5, 10)), rules).IsSuccess);
        Assert.Equal(
            "draws.results.incomplete_score",
            MatchResultValidator.Validate(Completed(MatchSide.Home, S(6, 4), S(6, 3)), rules).Error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-3)]
    public void ScoringRules_RejectsAnEvenOrNonPositiveBestOf(int bestOf)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScoringRules(bestOf));
    }

    [Fact]
    public void ScoringRules_DefaultIsBestOfThreeWithSuperTiebreak()
    {
        Assert.Equal(3, ScoringRules.Default.BestOf);
        Assert.True(ScoringRules.Default.ThirdSetSuperTiebreak);
        Assert.Equal(2, ScoringRules.Default.SetsToWin);
    }

    [Fact]
    public void MatchResult_CopiesItsSetsAndReportsWhetherItIsDecided()
    {
        var sets = new List<SetScore> { S(6, 4), S(6, 3) };
        var result = Completed(MatchSide.Home, [.. sets]);
        sets.Clear();

        Assert.Equal(2, result.Sets.Count);
        Assert.True(result.IsDecided);
        Assert.True(new MatchResult("M", MatchStatus.Walkover, winnerSide: MatchSide.Home).IsDecided);
        Assert.True(new MatchResult("M", MatchStatus.Retired, winnerSide: MatchSide.Home).IsDecided);
        Assert.False(new MatchResult("M", MatchStatus.InProgress).IsDecided);
        Assert.False(new MatchResult("M", MatchStatus.Scheduled).IsDecided);
        Assert.Empty(new MatchResult("M", MatchStatus.Scheduled).Sets);
    }

    [Fact]
    public void Validate_RequiresArguments()
    {
        Assert.Throws<ArgumentNullException>(() => MatchResultValidator.Validate(null!, ScoringRules.Default));
        Assert.Throws<ArgumentNullException>(() => MatchResultValidator.Validate(Completed(MatchSide.Home), null!));
    }
}
