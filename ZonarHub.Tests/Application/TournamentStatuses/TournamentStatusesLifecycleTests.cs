using ZonarHub.Application.Features.TournamentStatuses.Create;
using ZonarHub.Application.Features.TournamentStatuses.Delete;
using ZonarHub.Application.Features.TournamentStatuses.GetAll;
using ZonarHub.Application.Features.TournamentStatuses.GetById;
using ZonarHub.Application.Features.TournamentStatuses.Update;

namespace ZonarHub.Tests.Application.TournamentStatuses;

public class TournamentStatusesLifecycleTests
{
    private static readonly DateTime Now = new(2026, 6, 5, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FullLifecycle_CreateReadUpdateDelete_Works()
    {
        var h = new TournamentStatusesTestHarness(Now);

        var initial = await h.GetAll.Handle(new GetTournamentStatusesQuery(), CancellationToken.None);
        Assert.True(initial.IsSuccess);
        Assert.NotEmpty(initial.Value);

        var created = await h.Create.Handle(
            new CreateTournamentStatusCommand("Pendiente de Sorteo", "pending_draw", "Esperando armado", 90),
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal("pending_draw", created.Value.Key);

        var fetched = await h.GetById.Handle(new GetTournamentStatusByIdQuery(created.Value.Id), CancellationToken.None);
        Assert.True(fetched.IsSuccess);
        Assert.Equal(created.Value.Id, fetched.Value.Id);

        h.Clock.UtcNow = Now.AddHours(1);
        var updated = await h.Update.Handle(
            new UpdateTournamentStatusCommand(
                created.Value.Id,
                Name: "Sorteo en Proceso",
                Description: "Sorteo activo",
                SortOrder: 120,
                IsActive: false),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Equal("Sorteo en Proceso", updated.Value.Name);
        Assert.False(updated.Value.IsActive);
        Assert.Equal(Now.AddHours(1), updated.Value.UpdatedAt);

        var deleted = await h.Delete.Handle(new DeleteTournamentStatusCommand(created.Value.Id), CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        var missing = await h.GetById.Handle(new GetTournamentStatusByIdQuery(created.Value.Id), CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal("tournament_statuses.not_found", missing.Error.Code);
    }

    [Fact]
    public async Task Create_DuplicateKey_ReturnsConflict()
    {
        var h = new TournamentStatusesTestHarness(Now);

        var first = await h.Create.Handle(
            new CreateTournamentStatusCommand("Estado A", "same_key", null, 1),
            CancellationToken.None);

        var duplicate = await h.Create.Handle(
            new CreateTournamentStatusCommand("Estado B", "same_key", null, 2),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(duplicate.IsFailure);
        Assert.Equal("tournament_statuses.key_exists", duplicate.Error.Code);
    }

    [Fact]
    public async Task Update_DuplicateName_ReturnsConflict()
    {
        var h = new TournamentStatusesTestHarness(Now);

        var first = await h.Create.Handle(
            new CreateTournamentStatusCommand("Clasificacion", "classification", null, 10),
            CancellationToken.None);

        var second = await h.Create.Handle(
            new CreateTournamentStatusCommand("Eliminacion", "elimination", null, 20),
            CancellationToken.None);

        var duplicate = await h.Update.Handle(
            new UpdateTournamentStatusCommand(second.Value.Id, first.Value.Name, null, 30, true),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(duplicate.IsFailure);
        Assert.Equal("tournament_statuses.name_exists", duplicate.Error.Code);
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsNotFound()
    {
        var h = new TournamentStatusesTestHarness(Now);

        var deleted = await h.Delete.Handle(new DeleteTournamentStatusCommand("missing"), CancellationToken.None);

        Assert.True(deleted.IsFailure);
        Assert.Equal("tournament_statuses.not_found", deleted.Error.Code);
    }
}
