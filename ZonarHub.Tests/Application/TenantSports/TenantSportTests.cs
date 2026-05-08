using ZonarHub.Application.Features.Sports.Create;
using ZonarHub.Application.Features.TenantSports.Get;
using ZonarHub.Application.Features.TenantSports.Set;
using ZonarHub.Domain.Sports;

namespace ZonarHub.Tests.Application.TenantSports;

public class TenantSportTests
{
    private static readonly DateTime Now = new(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SetAndGet_EnabledSports_WorksCorrectly()
    {
        var h = new TenantSportsTestHarness(Now);
        var tenantId = Guid.NewGuid();

        var padel = await h.CreateSport.Handle(
            new CreateSportCommand("Padel", "padel", "tennis", SportIconSource.Unicode, new[] { Guid.NewGuid() },1),
            CancellationToken.None);
        var tennis = await h.CreateSport.Handle(
            new CreateSportCommand("Tennis", "tennis", "tennis", SportIconSource.Unicode, new[] { Guid.NewGuid() },2),
            CancellationToken.None);

        Assert.True(padel.IsSuccess);
        Assert.True(tennis.IsSuccess);

        var set = await h.SetTenantSports.Handle(
            new SetTenantSportsCommand(tenantId, new[] { padel.Value.Id }, tenantId),
            CancellationToken.None);

        Assert.True(set.IsSuccess);

        var result = await h.GetTenantSports.Handle(
            new GetTenantSportsQuery(tenantId, tenantId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.True(result.Value.Items.Single(i => i.Key == "padel").IsEnabled);
        Assert.False(result.Value.Items.Single(i => i.Key == "tennis").IsEnabled);
    }

    [Fact]
    public async Task SetSports_WithInvalidSportId_ReturnsNotFound()
    {
        var h = new TenantSportsTestHarness(Now);
        var tenantId = Guid.NewGuid();

        var result = await h.SetTenantSports.Handle(
            new SetTenantSportsCommand(tenantId, new[] { Guid.NewGuid() }, tenantId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("sports.not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetAndSet_CrossTenantScope_IsDenied()
    {
        var h = new TenantSportsTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var requiredTenantId = Guid.NewGuid();

        var getResult = await h.GetTenantSports.Handle(
            new GetTenantSportsQuery(tenantId, requiredTenantId),
            CancellationToken.None);

        Assert.True(getResult.IsFailure);
        Assert.Equal("tenant.not_found", getResult.Error.Code);

        var setResult = await h.SetTenantSports.Handle(
            new SetTenantSportsCommand(tenantId, Array.Empty<Guid>(), requiredTenantId),
            CancellationToken.None);

        Assert.True(setResult.IsFailure);
        Assert.Equal("tenant.not_found", setResult.Error.Code);
    }
}
