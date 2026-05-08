using ZonarHub.Application.Features.Complexes.Create;
using ZonarHub.Application.Features.Complexes.GetAll;

namespace ZonarHub.Tests.Application.Complexes;

public class ComplexLifecycleTests
{
    private static readonly Guid OrganizationId = Guid.Parse("3c5be98e-4bc7-4515-bc18-76a6ee3968c6");

    [Fact]
    public async Task Create_PersistsComplexAndCommitsUnitOfWork()
    {
        var h = new ComplexesTestHarness();

        var created = await h.Create.Handle(
            new CreateComplexCommand(
                OrganizationId,
                "Sample Venue",
                "sample",
                "123 Court Street",
                "Buenos Aires",
                "First venue",
                1,
                1,
                null,
                null,
                null),
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal("Sample Venue", created.Value.Name);

        // Regression guard: when the handler calls AddAsync against a
        // repository that enqueues writes (the Supabase pattern), it MUST
        // also flush the unit of work. Without this assertion the
        // "complex returned 201 but never reached the DB" bug slips
        // through because the in-memory repo writes synchronously.
        Assert.True(h.UnitOfWork.SaveCount >= 1, "CreateComplexHandler debe llamar a IUnitOfWork.SaveChangesAsync");

        var listed = await h.List.Handle(new GetComplexesQuery(OrganizationId), CancellationToken.None);
        Assert.True(listed.IsSuccess);
        Assert.Single(listed.Value);
        Assert.Equal(created.Value.Id, listed.Value[0].Id);
    }

    [Fact]
    public async Task List_AfterCreate_ReturnsPersistedComplex()
    {
        var h = new ComplexesTestHarness();

        await h.Create.Handle(
            new CreateComplexCommand(
                OrganizationId, "Venue One", "venue-one", "Addr 1",
                null, null, 1, 1, null, null, null),
            CancellationToken.None);

        await h.Create.Handle(
            new CreateComplexCommand(
                OrganizationId, "Venue Two", "venue-two", "Addr 2",
                null, null, 2, 2, null, null, null),
            CancellationToken.None);

        var listed = await h.List.Handle(new GetComplexesQuery(OrganizationId), CancellationToken.None);
        Assert.True(listed.IsSuccess);
        Assert.Equal(2, listed.Value.Count);
    }

    [Fact]
    public async Task List_DifferentOrganization_DoesNotLeakRows()
    {
        var h = new ComplexesTestHarness();

        await h.Create.Handle(
            new CreateComplexCommand(
                OrganizationId, "Venue", "venue", "Addr",
                null, null, 1, 1, null, null, null),
            CancellationToken.None);

        var otherOrg = Guid.NewGuid();
        var listed = await h.List.Handle(new GetComplexesQuery(otherOrg), CancellationToken.None);

        Assert.True(listed.IsSuccess);
        Assert.Empty(listed.Value);
    }
}
