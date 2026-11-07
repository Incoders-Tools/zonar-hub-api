using System.Net;
using System.Text;
using System.Text.Json;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.AdminPermissions;
using ZonarHub.Application.Features.AdminPermissions.ListPermissionSources;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Users;
using ZonarHub.Infrastructure.Persistence.Supabase;
using ZonarHub.Tests.Application.AdminUsers;

namespace ZonarHub.Tests.Application.AdminPermissions;

public class ListPermissionSourcesTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Unauthenticated_FailsClosed()
    {
        var h = new AdminUsersTestHarness(Now);
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "maria@zonarhub.dev", "Maria");

        var result = await Handler(h).Handle(new ListPermissionSourcesQuery("maria"), CancellationToken.None);

        AssertForbidden(result);
    }

    [Fact]
    public async Task UnknownCaller_FailsClosed()
    {
        var h = new AdminUsersTestHarness(Now);
        h.CurrentUser.Authenticate(Guid.NewGuid(), "ghost@zonarhub.dev");

        var result = await Handler(h).Handle(new ListPermissionSourcesQuery("maria"), CancellationToken.None);

        AssertForbidden(result);
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Editor)]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Player)]
    public async Task NonAdministratorRole_FailsClosed(UserRole role)
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(role, tenantId, "caller@zonarhub.dev", "Caller");
        await h.SeedUserAsync(UserRole.Viewer, tenantId, "maria@zonarhub.dev", "Maria");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new ListPermissionSourcesQuery("maria"), CancellationToken.None);

        AssertForbidden(result);
    }

    [Fact]
    public async Task TenantAdminWithoutTenant_FailsClosed()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId: null, "admin@zonarhub.dev", "Admin");
        await h.SeedUserAsync(UserRole.Viewer, tenantId: null, "maria@zonarhub.dev", "Maria");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new ListPermissionSourcesQuery("maria"), CancellationToken.None);

        AssertForbidden(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*")]
    [InlineData("%%")]
    [InlineData(" *%* ")]
    [InlineData("a")]
    [InlineData("*a%")]
    [InlineData("__")]
    [InlineData("_a_")]
    [InlineData("(),%")]
    [InlineData("(a),")]
    [InlineData("\"\\")]
    public async Task SearchWithoutTwoMeaningfulCharacters_ReturnsValidationError(string? search)
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "ana@zonarhub.dev", "Ana");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new ListPermissionSourcesQuery(search), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ListPermissionSourcesErrors.SearchTooShort, result.Error);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task TenantAdmin_SearchesOwnTenantOnly_ExcludingSystemAdminsBeforePagingAndCount()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "z-admin@zonarhub.dev", "Caller");
        await h.SeedUserAsync(UserRole.SystemAdmin, tenantId, "a-maria-root@zonarhub.dev", "Maria Root");
        await h.SeedUserAsync(UserRole.Editor, tenantId, "b-maria@zonarhub.dev", "Maria Editor");
        var inactive = await h.SeedUserAsync(UserRole.Viewer, tenantId, "c-maria@zonarhub.dev", "Maria Viewer");
        Deactivate(h, inactive);
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "a-maria-foreign@zonarhub.dev", "Maria Foreign");
        await h.SeedUserAsync(UserRole.Viewer, tenantId, "d-pedro@zonarhub.dev", "Pedro");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var firstPage = await Handler(h).Handle(new ListPermissionSourcesQuery("maria", Page: 1, PageSize: 1), CancellationToken.None);
        var secondPage = await Handler(h).Handle(new ListPermissionSourcesQuery("maria", Page: 2, PageSize: 1), CancellationToken.None);

        Assert.True(firstPage.IsSuccess);
        Assert.Equal(2, firstPage.Value.TotalCount);
        var first = Assert.Single(firstPage.Value.Items);
        Assert.Equal("b-maria@zonarhub.dev", first.Email);
        Assert.Equal("Maria Editor", first.FullName);
        Assert.Equal("role004", first.RoleId);
        Assert.True(first.IsActive);

        var second = Assert.Single(secondPage.Value.Items);
        Assert.Equal(inactive.Id.Value, second.Id);
        Assert.False(second.IsActive);
    }

    [Fact]
    public async Task SystemAdmin_SearchesAllTenantsIncludingSystemAdmins()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "maria-root@zonarhub.dev", "Maria Root");
        await h.SeedUserAsync(UserRole.Admin, Guid.NewGuid(), "maria-a@zonarhub.dev", "Maria A");
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "maria-b@zonarhub.dev", "Maria B");
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "pedro@zonarhub.dev", "Pedro");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new ListPermissionSourcesQuery("  MARIA "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(
            ["maria-a@zonarhub.dev", "maria-b@zonarhub.dev", "maria-root@zonarhub.dev"],
            result.Value.Items.Select(item => item.Email));
    }

    [Fact]
    public async Task Search_RemovesWildcardsInsteadOfBroadening()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "ana@zonarhub.dev", "Ana");
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "pedro@zonarhub.dev", "Pedro");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new ListPermissionSourcesQuery("%a*n%"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ana@zonarhub.dev", Assert.Single(result.Value.Items).Email);
    }

    [Fact]
    public async Task Search_PreservesUnderscoreForLegitimateEmailSearch()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "ana_lopez@zonarhub.dev", "Ana Lopez");
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "analopez@zonarhub.dev", "Ana L");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new ListPermissionSourcesQuery("ana_lopez"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ana_lopez@zonarhub.dev", Assert.Single(result.Value.Items).Email);
    }

    [Theory]
    [InlineData("__")]
    [InlineData("(),%")]
    [InlineData("_(,)_")]
    public async Task SupabaseRepository_RejectedSearch_SendsNoListRequest(string search)
    {
        var tenantId = Guid.NewGuid();
        var (handler, listRequests) = SupabaseBackedHandler("admin", tenantId);

        var result = await handler.Handle(new ListPermissionSourcesQuery(search), CancellationToken.None);

        Assert.Equal(ListPermissionSourcesErrors.SearchTooShort, result.Error);
        Assert.Empty(listRequests);
    }

    [Fact]
    public async Task SupabaseRepository_TenantAdmin_PunctuationCannotAlterOrExpression()
    {
        var tenantId = Guid.NewGuid();
        var (handler, listRequests) = SupabaseBackedHandler("admin", tenantId);

        var result = await handler.Handle(
            new ListPermissionSourcesQuery(" ma_ria),role.eq.system_admin,(\"\\ "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var path = Assert.Single(listRequests);
        var parts = path[(path.IndexOf('?') + 1)..].Split('&');
        Assert.Contains($"tenant_id=eq.{tenantId}", parts);
        Assert.Contains("role=neq.system_admin", parts);
        Assert.Contains("or=(email.ilike.*ma_riarole.eq.system_admin*,full_name.ilike.*ma_riarole.eq.system_admin*)", parts);
        Assert.Single(parts, part => part.StartsWith("or=", StringComparison.Ordinal));
        Assert.DoesNotContain("%2C", path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%28", path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%29", path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%22", path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%5C", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SupabaseRepository_SystemAdmin_SearchesGloballyWithoutTenantOrRoleFilters()
    {
        var (handler, listRequests) = SupabaseBackedHandler("system_admin", tenantId: null);

        var result = await handler.Handle(new ListPermissionSourcesQuery("(ana)"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var parts = Assert.Single(listRequests).Split('?')[1].Split('&');
        Assert.Contains("or=(email.ilike.*ana*,full_name.ilike.*ana*)", parts);
        Assert.DoesNotContain(parts, part => part.StartsWith("tenant_id=", StringComparison.Ordinal));
        Assert.DoesNotContain(parts, part => part.StartsWith("role=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 0, 1, ListPermissionSourcesLimits.MaxPageSize)]
    [InlineData(-3, 500, 1, ListPermissionSourcesLimits.MaxPageSize)]
    [InlineData(2, 5, 2, 5)]
    public async Task Paging_IsNormalizedAndCapped(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        for (var index = 0; index < 30; index++)
        {
            await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), $"maria{index:D2}@zonarhub.dev", $"Maria {index}");
        }

        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new ListPermissionSourcesQuery("maria", page, pageSize), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedPage, result.Value.Page);
        Assert.Equal(expectedPageSize, result.Value.PageSize);
        Assert.Equal(expectedPageSize, result.Value.Items.Count);
        Assert.Equal(30, result.Value.TotalCount);
    }

    private static ListPermissionSourcesHandler Handler(AdminUsersTestHarness h) => new(h.CurrentUser, h.Users);

    private static void Deactivate(AdminUsersTestHarness h, User user)
    {
        var update = user.UpdateAdminProfile(user.FullName, user.Phone, user.Role, isActive: false, user.OrganizationId, Now);
        Assert.True(update.IsSuccess);
        h.Users.Update(user);
    }

    /// <summary>
    /// Builds the handler over the real Supabase repository with a recording HTTP handler, so tests observe
    /// the exact PostgREST list request. The caller lookup is answered; list requests are recorded.
    /// </summary>
    private static (ListPermissionSourcesHandler Handler, List<string> ListRequests) SupabaseBackedHandler(
        string callerRole,
        Guid? tenantId)
    {
        var callerId = Guid.NewGuid();
        var listRequests = new List<string>();
        var http = new HttpClient(new RecordingHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains($"id=eq.{callerId}", StringComparison.Ordinal))
            {
                return Json(new[] { CallerRow(callerId, callerRole, tenantId) });
            }

            listRequests.Add(path);
            var response = Json(Array.Empty<object>());
            response.Content.Headers.TryAddWithoutValidation("Content-Range", "*/0");
            return response;
        }))
        {
            BaseAddress = new Uri("https://example.invalid"),
        };

        var currentUser = new TestCurrentUser();
        currentUser.Authenticate(callerId, "caller@example.test");
        var repository = new UserRepository(new ClientFactory(http), new SupabaseOperationContext());
        return (new ListPermissionSourcesHandler(currentUser, repository), listRequests);
    }

    private static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
    };

    private static Dictionary<string, object?> CallerRow(Guid id, string role, Guid? tenantId) => new()
    {
        ["id"] = id,
        ["email"] = "caller@example.test",
        ["full_name"] = "Caller",
        ["phone"] = null,
        ["birth_date"] = null,
        ["password_hash"] = "not-a-real-hash",
        ["role"] = role,
        ["tenant_id"] = tenantId,
        ["organization_id"] = null,
        ["avatar_url"] = null,
        ["locale"] = "es",
        ["date_format"] = null,
        ["is_email_verified"] = true,
        ["is_active"] = true,
        ["created_at_utc"] = "2026-10-02T10:00:00+00:00",
        ["updated_at_utc"] = "2026-10-02T11:00:00+00:00",
    };

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    private static void AssertForbidden(Result<PageResult<PermissionSourceUserResponse>> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(AdminPermissionErrors.Forbidden, result.Error);
    }
}
