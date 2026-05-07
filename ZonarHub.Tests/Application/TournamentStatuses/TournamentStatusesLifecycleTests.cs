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

        var created = await h.Create.Handle(
            new CreateTournamentStatusCommand(
                Key: "pending_draw",
                NameEs: "Pendiente de Sorteo",
                NameEn: "Pending Draw",
                NamePt: "Sorteio pendente",
                DescriptionEs: "Esperando armado",
                DescriptionEn: "Waiting for the draw",
                DescriptionPt: "Aguardando sorteio",
                SortOrder: 90),
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal("pending_draw", created.Value.Key);
        Assert.Equal("Pendiente de Sorteo", created.Value.NameEs);

        var createdId = Guid.Parse(created.Value.Id);
        var fetched = await h.GetById.Handle(new GetTournamentStatusByIdQuery(createdId), CancellationToken.None);
        Assert.True(fetched.IsSuccess);
        Assert.Equal(created.Value.Id, fetched.Value.Id);

        h.Clock.UtcNow = Now.AddHours(1);
        var updated = await h.Update.Handle(
            new UpdateTournamentStatusCommand(
                Id: createdId,
                NameEs: "Sorteo en Proceso",
                NameEn: "Draw In Progress",
                NamePt: "Sorteio em andamento",
                DescriptionEs: "Sorteo activo",
                DescriptionEn: "Active draw",
                DescriptionPt: "Sorteio ativo",
                SortOrder: 120,
                IsActive: false),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Equal("Sorteo en Proceso", updated.Value.NameEs);
        Assert.False(updated.Value.IsActive);
        Assert.Equal(Now.AddHours(1), updated.Value.UpdatedAt);

        var deleted = await h.Delete.Handle(new DeleteTournamentStatusCommand(createdId), CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        var missing = await h.GetById.Handle(new GetTournamentStatusByIdQuery(createdId), CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal("tournament_statuses.not_found", missing.Error.Code);
    }

    [Fact]
    public async Task Create_DuplicateKey_ReturnsConflict()
    {
        var h = new TournamentStatusesTestHarness(Now);

        var first = await h.Create.Handle(
            new CreateTournamentStatusCommand("same_key", "Estado A", "Status A", "Estado A", null, null, null, 1),
            CancellationToken.None);

        var duplicate = await h.Create.Handle(
            new CreateTournamentStatusCommand("same_key", "Estado B", "Status B", "Estado B", null, null, null, 2),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(duplicate.IsFailure);
        Assert.Equal("tournament_statuses.key_exists", duplicate.Error.Code);
    }

    [Fact]
    public async Task Update_PartialLocale_PreservesOtherTranslations()
    {
        var h = new TournamentStatusesTestHarness(Now);

        var created = await h.Create.Handle(
            new CreateTournamentStatusCommand(
                "classification",
                NameEs: "Clasificación",
                NameEn: "Classification",
                NamePt: "Classificação",
                DescriptionEs: "Fase de clasificación",
                DescriptionEn: null,
                DescriptionPt: null,
                SortOrder: 10),
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        var id = Guid.Parse(created.Value.Id);

        // Admin with EN locale only updates the English label.
        var updated = await h.Update.Handle(
            new UpdateTournamentStatusCommand(
                Id: id,
                NameEs: "",            // blank → preserve previous
                NameEn: "Group Stage",
                NamePt: "",
                DescriptionEs: null,   // null → preserve previous
                DescriptionEn: "Round-robin phase",
                DescriptionPt: null,
                SortOrder: 10,
                IsActive: true),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Equal("Clasificación", updated.Value.NameEs);
        Assert.Equal("Group Stage", updated.Value.NameEn);
        Assert.Equal("Classificação", updated.Value.NamePt);
        Assert.Equal("Fase de clasificación", updated.Value.DescriptionEs);
        Assert.Equal("Round-robin phase", updated.Value.DescriptionEn);
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsNotFound()
    {
        var h = new TournamentStatusesTestHarness(Now);

        var deleted = await h.Delete.Handle(
            new DeleteTournamentStatusCommand(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(deleted.IsFailure);
        Assert.Equal("tournament_statuses.not_found", deleted.Error.Code);
    }
}
