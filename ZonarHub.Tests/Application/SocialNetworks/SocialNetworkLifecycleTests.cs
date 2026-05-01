using ZonarHub.Application.Features.SocialNetworks.Create;
using ZonarHub.Application.Features.SocialNetworks.Delete;
using ZonarHub.Application.Features.SocialNetworks.GetAll;
using ZonarHub.Application.Features.SocialNetworks.GetById;
using ZonarHub.Application.Features.SocialNetworks.Update;

namespace ZonarHub.Tests.Application.SocialNetworks;

public class SocialNetworkLifecycleTests
{
    private static readonly DateTime Now = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FullLifecycle_CreateReadUpdateDelete_Works()
    {
        var h = new SocialNetworksTestHarness(Now);

        // Create
        var created = await h.Create.Handle(
            new CreateSocialNetworkCommand("Instagram", "instagram", "https://instagram.com", null, "fa-instagram", 1),
            CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal("instagram", created.Value.Key);

        // Get by id
        var fetched = await h.GetById.Handle(
            new GetSocialNetworkByIdQuery(created.Value.Id),
            CancellationToken.None);
        Assert.True(fetched.IsSuccess);
        Assert.Equal(created.Value.Id, fetched.Value.Id);

        // List
        var listed = await h.List.Handle(
            new GetSocialNetworksQuery(new SocialNetworkFilter()),
            CancellationToken.None);
        Assert.True(listed.IsSuccess);
        Assert.Equal(1, listed.Value.TotalCount);

        // Update
        h.Clock.UtcNow = Now.AddHours(1);
        var updated = await h.Update.Handle(
            new UpdateSocialNetworkCommand(created.Value.Id, "Instagram Updated", null, null, "fa-instagram", 2, true),
            CancellationToken.None);
        Assert.True(updated.IsSuccess);
        Assert.Equal("Instagram Updated", updated.Value.Name);
        Assert.Equal(Now.AddHours(1), updated.Value.UpdatedAt);

        // Delete
        var deleted = await h.Delete.Handle(
            new DeleteSocialNetworkCommand(created.Value.Id),
            CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        // Confirm gone
        var missing = await h.GetById.Handle(
            new GetSocialNetworkByIdQuery(created.Value.Id),
            CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal("social_networks.not_found", missing.Error.Code);
    }

    [Fact]
    public async Task Create_DuplicateKey_ReturnsConflict()
    {
        var h = new SocialNetworksTestHarness(Now);

        await h.Create.Handle(
            new CreateSocialNetworkCommand("Twitter", "twitter", null, null, null, 1),
            CancellationToken.None);

        var duplicate = await h.Create.Handle(
            new CreateSocialNetworkCommand("Twitter X", "twitter", null, null, null, 2),
            CancellationToken.None);

        Assert.True(duplicate.IsFailure);
        Assert.Equal("social_networks.key_exists", duplicate.Error.Code);
    }

    [Fact]
    public async Task List_FilterByActiveStatus_ReturnsOnlyMatching()
    {
        var h = new SocialNetworksTestHarness(Now);

        await h.Create.Handle(new CreateSocialNetworkCommand("Active", "active-net", null, null, null, 1), CancellationToken.None);
        var inactive = await h.Create.Handle(new CreateSocialNetworkCommand("Inactive", "inactive-net", null, null, null, 2), CancellationToken.None);

        // Deactivate the second
        await h.Update.Handle(
            new UpdateSocialNetworkCommand(inactive.Value.Id, "Inactive", null, null, null, 2, false),
            CancellationToken.None);

        var activeOnly = await h.List.Handle(
            new GetSocialNetworksQuery(new SocialNetworkFilter(IsActive: true)),
            CancellationToken.None);

        Assert.True(activeOnly.IsSuccess);
        Assert.Equal(1, activeOnly.Value.TotalCount);
        Assert.All(activeOnly.Value.Items, n => Assert.True(n.IsActive));
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var h = new SocialNetworksTestHarness(Now);

        var result = await h.GetById.Handle(
            new GetSocialNetworkByIdQuery(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social_networks.not_found", result.Error.Code);
    }
}
