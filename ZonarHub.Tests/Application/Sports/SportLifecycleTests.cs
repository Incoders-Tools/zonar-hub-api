using ZonarHub.Application.Features.Sports.Create;
using ZonarHub.Application.Features.Sports.Delete;
using ZonarHub.Application.Features.Sports.GetAll;
using ZonarHub.Application.Features.Sports.GetById;
using ZonarHub.Application.Features.Sports.Update;
using ZonarHub.Domain.Sports;

namespace ZonarHub.Tests.Application.Sports;

public class SportLifecycleTests
{
    private static readonly DateTime Now = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FullLifecycle_CreateReadUpdateDelete_Works()
    {
        var h = new SportsTestHarness(Now);

        // Create
        var created = await h.Create.Handle(
            new CreateSportCommand("Padel", "padel", "🎾", SportIconSource.Unicode, null, 1),
            CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal("padel", created.Value.Key);
        Assert.Equal("unicode", created.Value.IconSource);

        // Get by id
        var fetched = await h.GetById.Handle(
            new GetSportByIdQuery(created.Value.Id),
            CancellationToken.None);
        Assert.True(fetched.IsSuccess);
        Assert.Equal(created.Value.Id, fetched.Value.Id);

        // List
        var listed = await h.List.Handle(
            new GetSportsQuery(new SportFilter()),
            CancellationToken.None);
        Assert.True(listed.IsSuccess);
        Assert.Equal(1, listed.Value.TotalCount);

        // Update
        h.Clock.UtcNow = Now.AddHours(1);
        var updated = await h.Update.Handle(
            new UpdateSportCommand(created.Value.Id, "Padel Pro", "🏓", SportIconSource.Unicode, null, 2, true),
            CancellationToken.None);
        Assert.True(updated.IsSuccess);
        Assert.Equal("Padel Pro", updated.Value.Name);
        Assert.Equal(Now.AddHours(1), updated.Value.UpdatedAt);

        // Delete
        var deleted = await h.Delete.Handle(
            new DeleteSportCommand(created.Value.Id),
            CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        // Confirm gone
        var missing = await h.GetById.Handle(
            new GetSportByIdQuery(created.Value.Id),
            CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal("sports.not_found", missing.Error.Code);
    }

    [Fact]
    public async Task Create_DuplicateKey_ReturnsConflict()
    {
        var h = new SportsTestHarness(Now);

        await h.Create.Handle(
            new CreateSportCommand("Tennis", "tennis", "🎾", SportIconSource.Unicode, null, 1),
            CancellationToken.None);

        var duplicate = await h.Create.Handle(
            new CreateSportCommand("Tennis Pro", "tennis", "🎾", SportIconSource.Unicode, null, 2),
            CancellationToken.None);

        Assert.True(duplicate.IsFailure);
        Assert.Equal("sports.key_exists", duplicate.Error.Code);
    }

    [Fact]
    public async Task Create_WithModalityIds_StoresAndReturnsIds()
    {
        var h = new SportsTestHarness(Now);
        var modalityId = Guid.NewGuid();

        var created = await h.Create.Handle(
            new CreateSportCommand("Padel", "padel", "🎾", SportIconSource.Unicode, new[] { modalityId }, 1),
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Contains(modalityId, created.Value.ModalityIds);
    }

    [Fact]
    public async Task List_FilterByName_ReturnsOnlyMatching()
    {
        var h = new SportsTestHarness(Now);

        await h.Create.Handle(new CreateSportCommand("Padel", "padel", "🎾", SportIconSource.Unicode, null, 1), CancellationToken.None);
        await h.Create.Handle(new CreateSportCommand("Tennis", "tennis", "🎾", SportIconSource.Unicode, null, 2), CancellationToken.None);

        var filtered = await h.List.Handle(
            new GetSportsQuery(new SportFilter(NameContains: "pad")),
            CancellationToken.None);

        Assert.True(filtered.IsSuccess);
        Assert.Equal(1, filtered.Value.TotalCount);
        Assert.Equal("Padel", filtered.Value.Items[0].Name);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var h = new SportsTestHarness(Now);

        var result = await h.GetById.Handle(
            new GetSportByIdQuery(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("sports.not_found", result.Error.Code);
    }
}
