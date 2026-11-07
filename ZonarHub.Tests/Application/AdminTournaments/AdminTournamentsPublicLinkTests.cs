using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.AdminTournaments.Create;
using ZonarHub.Application.Features.AdminTournaments.GetById;
using ZonarHub.Application.Features.AdminTournaments.Update;
using ZonarHub.Domain.Users;

namespace ZonarHub.Tests.Application.AdminTournaments;

/// <summary>
/// Tournament visibility and share link: the API generates the public slug the first time a tournament is
/// saved public and never regenerates it; clients cannot send one.
/// </summary>
public class AdminTournamentsPublicLinkTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Regex SlugFormat = new("^open-cup-[a-z0-9]{6}$");

    [Fact]
    public async Task Create_Public_GeneratesSlugFromTheNameAndPersistsIt()
    {
        var (h, orgId) = await OwnTenantAsync();

        var result = await h.Create.Handle(
            AdminTournamentsItemAccessTests.CreateCommand(orgId) with { IsPublic = true },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsPublic);
        Assert.Matches(SlugFormat, result.Value.PublicSlug);
        var stored = Assert.Single(h.Tournaments.Added);
        Assert.True(stored.IsPublic);
        Assert.Equal(result.Value.PublicSlug, stored.PublicSlug);
    }

    [Fact]
    public async Task Create_NotPublic_LeavesSlugNull()
    {
        var (h, orgId) = await OwnTenantAsync();

        var result = await h.Create.Handle(AdminTournamentsItemAccessTests.CreateCommand(orgId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsPublic);
        Assert.Null(result.Value.PublicSlug);
        Assert.Null(Assert.Single(h.Tournaments.Added).PublicSlug);
    }

    [Fact]
    public async Task Update_FirstTimePublic_GeneratesSlug()
    {
        var (h, orgId) = await OwnTenantAsync();
        var tournament = h.Tournaments.Seed(orgId, "Open A");

        var result = await h.Update.Handle(
            AdminTournamentsItemAccessTests.UpdateCommand(tournament.Id) with { Name = "Open Cup", IsPublic = true },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsPublic);
        Assert.Matches(SlugFormat, result.Value.PublicSlug);
        Assert.Equal(result.Value.PublicSlug, Assert.Single(h.Tournaments.Updated).PublicSlug);
    }

    [Fact]
    public async Task Update_TogglingOffAndOn_KeepsTheSameSlug()
    {
        var (h, orgId) = await OwnTenantAsync();
        var tournament = h.Tournaments.Seed(orgId, "Open A", isPublic: true, publicSlug: "open-a-abc123");

        var off = await h.Update.Handle(
            AdminTournamentsItemAccessTests.UpdateCommand(tournament.Id) with { IsPublic = false },
            CancellationToken.None);
        var on = await h.Update.Handle(
            AdminTournamentsItemAccessTests.UpdateCommand(tournament.Id) with { Name = "Another Name", IsPublic = true },
            CancellationToken.None);

        Assert.False(off.Value.IsPublic);
        Assert.Equal("open-a-abc123", off.Value.PublicSlug);
        Assert.True(on.Value.IsPublic);
        Assert.Equal("open-a-abc123", on.Value.PublicSlug);
        Assert.All(h.Tournaments.Updated, row => Assert.Equal("open-a-abc123", row.PublicSlug));
    }

    [Fact]
    public async Task Update_NotPublicWithoutSlug_KeepsSlugNull()
    {
        var (h, orgId) = await OwnTenantAsync();
        var tournament = h.Tournaments.Seed(orgId, "Open A");

        var result = await h.Update.Handle(AdminTournamentsItemAccessTests.UpdateCommand(tournament.Id), CancellationToken.None);

        Assert.False(result.Value.IsPublic);
        Assert.Null(result.Value.PublicSlug);
    }

    [Fact]
    public async Task Create_SlugCollision_RetriesWithANewSlug()
    {
        var (h, orgId) = await OwnTenantAsync();
        h.Tournaments.PublicSlugConflictsToThrow = 2;

        var result = await h.Create.Handle(
            AdminTournamentsItemAccessTests.CreateCommand(orgId) with { IsPublic = true },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, h.Tournaments.AttemptedPublicSlugs.Count);
        Assert.All(h.Tournaments.AttemptedPublicSlugs, slug => Assert.Matches(SlugFormat, slug));
        Assert.Equal(result.Value.PublicSlug, h.Tournaments.AttemptedPublicSlugs[^1]);
        Assert.Single(h.Tournaments.Added);
    }

    [Fact]
    public async Task Update_SlugCollision_RetriesWithANewSlug()
    {
        var (h, orgId) = await OwnTenantAsync();
        var tournament = h.Tournaments.Seed(orgId, "Open Cup");
        h.Tournaments.PublicSlugConflictsToThrow = 1;

        var result = await h.Update.Handle(
            AdminTournamentsItemAccessTests.UpdateCommand(tournament.Id) with { Name = "Open Cup", IsPublic = true },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, h.Tournaments.AttemptedPublicSlugs.Count);
        Assert.Matches(SlugFormat, result.Value.PublicSlug);
    }

    [Fact]
    public async Task Create_SlugCollisionOnEveryAttempt_StopsAfterTheBoundedRetries()
    {
        var (h, orgId) = await OwnTenantAsync();
        h.Tournaments.PublicSlugConflictsToThrow = int.MaxValue;

        await Assert.ThrowsAsync<TournamentPublicSlugConflictException>(() => h.Create.Handle(
            AdminTournamentsItemAccessTests.CreateCommand(orgId) with { IsPublic = true },
            CancellationToken.None));

        Assert.Equal(TournamentPublicSlugGeneration.MaxAttempts, h.Tournaments.AttemptedPublicSlugs.Count);
        Assert.Empty(h.Tournaments.Added);
    }

    [Fact]
    public async Task Update_ConflictOnAnExistingSlug_IsNotRetried()
    {
        var (h, orgId) = await OwnTenantAsync();
        var tournament = h.Tournaments.Seed(orgId, "Open A", isPublic: true, publicSlug: "open-a-abc123");
        h.Tournaments.PublicSlugConflictsToThrow = 1;

        await Assert.ThrowsAsync<TournamentPublicSlugConflictException>(() => h.Update.Handle(
            AdminTournamentsItemAccessTests.UpdateCommand(tournament.Id) with { IsPublic = true },
            CancellationToken.None));

        Assert.Equal(["open-a-abc123"], h.Tournaments.AttemptedPublicSlugs);
    }

    [Fact]
    public async Task Update_ForeignTenantPublicRequest_IsForbiddenBeforeAnySlugIsGenerated()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var foreignOrg = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var tournament = h.Tournaments.Seed(foreignOrg.Id.Value, "Foreign Open");
        await AdminTournamentsItemAccessTests.AuthenticateAsync(h, UserRole.Admin, Guid.NewGuid());

        var result = await h.Update.Handle(
            AdminTournamentsItemAccessTests.UpdateCommand(tournament.Id) with { IsPublic = true },
            CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.Forbidden, result.Error);
        Assert.Empty(h.Tournaments.AttemptedPublicSlugs);
    }

    [Fact]
    public async Task GetById_ExposesVisibilityAndSlug()
    {
        var (h, orgId) = await OwnTenantAsync();
        var tournament = h.Tournaments.Seed(orgId, "Open A", isPublic: true, publicSlug: "open-a-abc123");

        var result = await h.GetById.Handle(new GetTournamentByIdQuery(tournament.Id), CancellationToken.None);

        Assert.True(result.Value.IsPublic);
        Assert.Equal("open-a-abc123", result.Value.PublicSlug);
    }

    [Fact]
    public async Task CreateEndpoint_ForwardsIsPublicAndReturnsTheGeneratedSlug()
    {
        var (h, orgId) = await OwnTenantAsync();
        var request = new CreateTournamentRequest(
            orgId, "Open Cup", null, null, Guid.NewGuid(), null, null, null, null, null, null,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2), null, null, null, null, null, null, null,
            null, null, null, null, null, true, null, IsPublic: true);

        var result = await AdminTournamentsEndpointsExtensions.CreateAsync(request, new DirectSender(h), CancellationToken.None);

        var created = Assert.IsType<Created<TournamentResponse>>(result).Value!;
        Assert.True(created.IsPublic);
        Assert.Matches(SlugFormat, created.PublicSlug);
    }

    [Fact]
    public async Task UpdateEndpoint_ForwardsIsPublic()
    {
        var (h, orgId) = await OwnTenantAsync();
        var tournament = h.Tournaments.Seed(orgId, "Open Cup");
        var request = new UpdateTournamentRequest(
            "Open Cup", null, null, Guid.NewGuid(), null, null, null, null, null, null,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2), null, null, null, null, null, null, null,
            null, null, null, null, null, true, null, IsPublic: true);

        var result = await AdminTournamentsEndpointsExtensions.UpdateAsync(
            tournament.Id, request, new DirectSender(h), CancellationToken.None);

        var updated = Assert.IsType<Ok<TournamentResponse>>(result).Value!;
        Assert.True(updated.IsPublic);
        Assert.Matches(SlugFormat, updated.PublicSlug);
    }

    [Theory]
    [InlineData(typeof(CreateTournamentRequest))]
    [InlineData(typeof(UpdateTournamentRequest))]
    [InlineData(typeof(CreateTournamentCommand))]
    [InlineData(typeof(UpdateTournamentCommand))]
    public void WriteContracts_HaveIsPublicButNoPublicSlug(Type contract)
    {
        Assert.NotNull(contract.GetProperty("IsPublic"));
        Assert.Null(contract.GetProperty("PublicSlug"));
    }

    [Fact]
    public void CreateRequest_OmittingIsPublic_DefaultsToNotPublicAndIgnoresAForgedSlug()
    {
        var create = System.Text.Json.JsonSerializer.Deserialize<CreateTournamentRequest>(
            """{"organizationId":"6f1c2a4e-0000-0000-0000-000000000001","name":"Open","sportId":"6f1c2a4e-0000-0000-0000-000000000002","startDate":"2026-06-01","endDate":"2026-06-02","isActive":true,"publicSlug":"forged-slug1"}""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        Assert.False(create.IsPublic);
    }

    private static async Task<(AdminTournamentsListTestHarness Harness, Guid OrganizationId)> OwnTenantAsync()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.Users.SeedOrganizationAsync(tenant, "Org A");
        await AdminTournamentsItemAccessTests.AuthenticateAsync(h, UserRole.Admin, tenant);
        return (h, org.Id.Value);
    }

    private sealed class DirectSender(AdminTournamentsListTestHarness h) : MediatR.ISender
    {
        public async Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                CreateTournamentCommand command => await h.Create.Handle(command, cancellationToken),
                UpdateTournamentCommand command => await h.Update.Handle(command, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name),
            };
            return (TResponse)result;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : MediatR.IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(MediatR.IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
