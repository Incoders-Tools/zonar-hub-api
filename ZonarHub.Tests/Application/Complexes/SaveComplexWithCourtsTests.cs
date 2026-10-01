using System.Net;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Complexes.SaveWithCourts;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Application.AdminUsers;
using Xunit;

namespace ZonarHub.Tests.Application.Complexes;

public class SaveComplexWithCourtsTests
{
    private static readonly DateTime Now = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SaveComplexWithCourtsCommand_ExposesAggregateIdentity()
    {
        var command = new SaveComplexWithCourtsCommand(null, Guid.NewGuid(), "Venue", "Street", [], []);
        Assert.Null(command.ComplexId);
        Assert.Equal("Venue", command.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssignedAdmin_SavesAggregateOnce_WithExpectedData(bool update)
    {
        var h = await Setup(UserRole.Admin);
        var id = Guid.NewGuid();
        if (update) h.Repository.Existing = Existing(id, h.OrganizationId);
        var court = new SaveCourtData(null, "Court", true, "hard", false, [Guid.NewGuid()]);
        var command = new SaveComplexWithCourtsCommand(update ? id : null, h.OrganizationId,
            " Venue ", " Street ", [court], [Guid.NewGuid()]);

        var result = await h.Handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(id == result.Value.ComplexId, update);
        Assert.Equal(1, h.Repository.SaveCount);
        var data = Assert.IsType<SaveComplexWithCourtsData>(h.Repository.Saved);
        Assert.Equal(command.ComplexId, data.ComplexId);
        Assert.Equal(h.OrganizationId, data.OrganizationId);
        Assert.Equal("Venue", data.Name);
        Assert.Equal("Street", data.Address);
        Assert.Same(command.Courts, data.Courts);
        Assert.Same(command.DeleteCourtIds, data.DeleteCourtIds);
    }

    [Theory]
    [InlineData(UserRole.Player, false)]
    [InlineData(UserRole.Admin, true)]
    public async Task UnauthorizedCaller_DoesNotSave(UserRole role, bool inactive)
    {
        var h = await Setup(role);
        if (inactive) h.User.UpdateAdminProfile("Test User", null, role, false, null, Now);
        var result = await h.Handler.Handle(Command(h.OrganizationId), CancellationToken.None);
        Assert.Equal("complexes.aggregate_forbidden", result.Error.Code);
        Assert.Equal(0, h.Repository.SaveCount);
    }

    [Fact]
    public async Task AnonymousCaller_DoesNotSave()
    {
        var h = await Setup(UserRole.Admin);
        h.CurrentUser.SignOut();
        var result = await h.Handler.Handle(Command(h.OrganizationId), CancellationToken.None);
        Assert.Equal("complexes.aggregate_forbidden", result.Error.Code);
        Assert.Equal(0, h.Repository.SaveCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Admin_CrossTenantOrUnassigned_DoesNotSave(bool crossTenant)
    {
        var h = await Setup(UserRole.Admin);
        var other = await h.SeedOrganization(crossTenant ? Guid.NewGuid() : h.TenantId);
        var result = await h.Handler.Handle(Command(other.Id.Value), CancellationToken.None);
        Assert.Equal("complexes.aggregate_forbidden", result.Error.Code);
        Assert.Equal(0, h.Repository.SaveCount);
    }

    [Fact]
    public async Task SystemAdmin_CanSaveInAnyActiveOrganization()
    {
        var h = await Setup(UserRole.SystemAdmin);
        var other = await h.SeedOrganization(Guid.NewGuid());
        var result = await h.Handler.Handle(Command(other.Id.Value), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, h.Repository.SaveCount);
        Assert.Equal(other.Id.Value, h.Repository.Saved!.OrganizationId);
    }

    [Fact]
    public async Task InactiveOrganization_DoesNotSave()
    {
        var h = await Setup(UserRole.SystemAdmin);
        var org = await h.Organizations.GetByIdAsync(new OrganizationId(h.OrganizationId));
        org!.Update("Club", null, null, OrganizationType.Estandar, null, false, Now);
        var result = await h.Handler.Handle(Command(h.OrganizationId), CancellationToken.None);
        Assert.Equal("complexes.aggregate_invalid", result.Error.Code);
        Assert.Equal(0, h.Repository.SaveCount);
    }

    [Fact]
    public async Task ExistingComplexFromAnotherOrganization_DoesNotSave()
    {
        var h = await Setup(UserRole.Admin);
        var id = Guid.NewGuid();
        h.Repository.Existing = Existing(id, Guid.NewGuid());
        var result = await h.Handler.Handle(Command(h.OrganizationId) with { ComplexId = id }, CancellationToken.None);
        Assert.Equal("complexes.aggregate_invalid", result.Error.Code);
        Assert.Equal(0, h.Repository.SaveCount);
    }

    [Fact]
    public async Task MissingExistingComplex_DoesNotSave()
    {
        var h = await Setup(UserRole.Admin);
        var result = await h.Handler.Handle(Command(h.OrganizationId) with { ComplexId = Guid.NewGuid() }, CancellationToken.None);
        Assert.Equal("complexes.aggregate_invalid", result.Error.Code);
        Assert.Equal(0, h.Repository.SaveCount);
    }

    [Fact]
    public async Task MalformedAggregate_DoesNotInvokeWrite()
    {
        var h = await Setup(UserRole.Admin);
        var id = Guid.NewGuid();
        var sport = Guid.NewGuid();
        var court = new SaveCourtData(id, "Court", true, null, false, [sport]);
        var valid = Command(h.OrganizationId);
        var invalid = new[]
        {
            valid with { OrganizationId = Guid.Empty },
            valid with { ComplexId = Guid.Empty },
            valid with { Name = " " },
            valid with { Address = " " },
            valid with { Courts = null! },
            valid with { DeleteCourtIds = null! },
            valid with { Courts = [court, court] },
            valid with { Courts = [court], DeleteCourtIds = [id] },
            valid with { DeleteCourtIds = [id, id] },
            valid with { DeleteCourtIds = [Guid.Empty] },
            valid with { Courts = [court with { Id = Guid.Empty }] },
            valid with { Courts = [court with { Name = " " }] },
            valid with { Courts = [court with { SportIds = [sport, sport] }] },
            valid with { Courts = [court with { SportIds = [Guid.Empty] }] }
        };
        foreach (var command in invalid)
        {
            var result = await h.Handler.Handle(command, CancellationToken.None);
            Assert.Equal("complexes.aggregate_invalid", result.Error.Code);
        }
        Assert.Equal(0, h.Repository.SaveCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "complexes.aggregate_invalid")]
    [InlineData(HttpStatusCode.Conflict, "complexes.aggregate_conflict")]
    public async Task ExpectedRpcRejection_ReturnsStableError(HttpStatusCode status, string code)
    {
        var h = await Setup(UserRole.Admin);
        h.Repository.Failure = new HttpRequestException("SQL details must not escape", null, status);
        var result = await h.Handler.Handle(Command(h.OrganizationId), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error.Code);
        Assert.DoesNotContain("SQL details", result.Error.MessageKey);
        Assert.Equal(1, h.Repository.SaveCount);
    }

    [Fact]
    public async Task UnexpectedRpcFailure_Propagates()
    {
        var h = await Setup(UserRole.Admin);
        h.Repository.Failure = new HttpRequestException("Server failure", null, HttpStatusCode.InternalServerError);
        await Assert.ThrowsAsync<HttpRequestException>(() => h.Handler.Handle(Command(h.OrganizationId), CancellationToken.None));
        Assert.Equal(1, h.Repository.SaveCount);
    }

    private static SaveComplexWithCourtsCommand Command(Guid orgId) => new(null, orgId, "Venue", "Street", [], []);

    private static Complex Existing(Guid id, Guid orgId) => Complex.Reconstitute(new ComplexId(id),
        new OrganizationId(orgId), "Existing", null, "Street", null, null, 0, 0,
        null, null, null, true, Now, Now);

    private static async Task<Fixture> Setup(UserRole role)
    {
        var tenantId = Guid.NewGuid();
        var users = new InMemoryUserRepository(new InMemoryUserStore());
        var organizations = new InMemoryOrganizationRepository(new InMemoryOrganizationStore());
        var assignments = new InMemoryUserOrganizationAssignmentRepository(new InMemoryUserOrganizationAssignmentStore());
        var user = User.Register(UserId.New(), $"{Guid.NewGuid()}@example.com", "Test User", null, null,
            "hash", role, role == UserRole.SystemAdmin ? null : tenantId, Now).Value;
        await users.AddAsync(user);
        var org = Organization.Create(OrganizationId.New(), tenantId, "Club", null, null,
            OrganizationType.Estandar, null, user.Id.Value, Now).Value;
        await organizations.AddAsync(org);
        await assignments.SetOrganizationIdsAsync(user.Id.Value, [org.Id.Value]);
        var current = new TestCurrentUser();
        current.Authenticate(user.Id.Value, "test@example.com");
        var repository = new CapturingComplexRepository();
        return new Fixture(new SaveComplexWithCourtsHandler(current, users, assignments, organizations, repository),
            current, user, organizations, repository, tenantId, org.Id.Value);
    }

    private sealed record Fixture(SaveComplexWithCourtsHandler Handler, TestCurrentUser CurrentUser,
        User User, InMemoryOrganizationRepository Organizations, CapturingComplexRepository Repository,
        Guid TenantId, Guid OrganizationId)
    {
        public async Task<Organization> SeedOrganization(Guid tenantId)
        {
            var org = Organization.Create(ZonarHub.Domain.Organizations.OrganizationId.New(), tenantId, "Other Club", null, null,
                OrganizationType.Estandar, null, User.Id.Value, Now).Value;
            await Organizations.AddAsync(org);
            return org;
        }
    }

    private sealed class CapturingComplexRepository : IComplexRepository
    {
        public Complex? Existing { get; set; }
        public SaveComplexWithCourtsData? Saved { get; private set; }
        public int SaveCount { get; private set; }
        public HttpRequestException? Failure { get; set; }
        public Task<SavedComplexWithCourtsData> SaveWithCourtsAsync(SaveComplexWithCourtsData data, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            Saved = data;
            if (Failure is not null) throw Failure;
            return Task.FromResult(new SavedComplexWithCourtsData(data.ComplexId ?? Guid.NewGuid(), data.Courts.Count, []));
        }
        public Task<Complex?> GetByIdAsync(ComplexId id, CancellationToken cancellationToken = default)
            => Task.FromResult(Existing?.Id == id ? Existing : null);
        public Task<IReadOnlyList<Complex>> ListByOrganizationAsync(OrganizationId id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task AddAsync(Complex complex, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(Complex complex, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(ComplexId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
