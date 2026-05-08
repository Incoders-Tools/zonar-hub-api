using ZonarHub.Application.Features.TournamentRules.Create;
using ZonarHub.Application.Features.TournamentRules.Delete;
using ZonarHub.Application.Features.TournamentRules.GetAll;
using ZonarHub.Application.Features.TournamentRules.GetById;
using ZonarHub.Application.Features.TournamentRules.Update;

namespace ZonarHub.Tests.Application.TournamentRules;

public class TournamentRulesLifecycleTests
{
    private static readonly DateTime Now = new(2026, 5, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FullLifecycle_CreateReadUpdateDelete_Works()
    {
        var h = new TournamentRulesTestHarness(Now);

        var created = await h.Create.Handle(
            new CreateTournamentRuleCommand(
                "Best of 3",
                "Tres sets",
                "Three sets",
                "Três sets",
                1),
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal("Best of 3", created.Value.Name);
        Assert.Equal("Tres sets", created.Value.DescriptionEs);
        Assert.True(created.Value.IsActive);

        var fetched = await h.GetById.Handle(
            new GetTournamentRuleByIdQuery(created.Value.Id),
            CancellationToken.None);
        Assert.True(fetched.IsSuccess);
        Assert.Equal(created.Value.Id, fetched.Value.Id);

        var listed = await h.GetAll.Handle(new GetTournamentRulesQuery(true), CancellationToken.None);
        Assert.True(listed.IsSuccess);
        Assert.Single(listed.Value);

        h.Clock.UtcNow = Now.AddHours(1);
        var updated = await h.Update.Handle(
            new UpdateTournamentRuleCommand(
                created.Value.Id,
                "Best of 5",
                "Cinco sets",
                "Five sets",
                "Cinco sets",
                2,
                false),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Equal("Best of 5", updated.Value.Name);
        Assert.False(updated.Value.IsActive);
        Assert.Equal(Now.AddHours(1), updated.Value.UpdatedAt);

        var listedActive = await h.GetAll.Handle(new GetTournamentRulesQuery(false), CancellationToken.None);
        Assert.True(listedActive.IsSuccess);
        Assert.Empty(listedActive.Value);

        var deleted = await h.Delete.Handle(new DeleteTournamentRuleCommand(created.Value.Id), CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        var missing = await h.GetById.Handle(
            new GetTournamentRuleByIdQuery(created.Value.Id),
            CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal("tournament_rules.not_found", missing.Error.Code);
    }

    [Fact]
    public async Task Create_BlankName_ReturnsValidationError()
    {
        var h = new TournamentRulesTestHarness(Now);

        var result = await h.Create.Handle(
            new CreateTournamentRuleCommand("   ", null, null, null, 0),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("tournament_rules.name_required", result.Error.Code);
    }

    [Fact]
    public async Task Update_NonExistent_ReturnsNotFound()
    {
        var h = new TournamentRulesTestHarness(Now);

        var result = await h.Update.Handle(
            new UpdateTournamentRuleCommand(Guid.NewGuid(), "x", null, null, null, 0, true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("tournament_rules.not_found", result.Error.Code);
    }
}
