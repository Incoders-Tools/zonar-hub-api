using System.Net;
using System.Text;
using System.Text.Json;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Users;
using ZonarHub.Infrastructure.Persistence.Supabase;
using Xunit;

namespace ZonarHub.Tests.Infrastructure.Persistence;

public class UserRepositoryListTests
{
    private const string RpcPath = "/rest/v1/rpc/list_admin_users_by_organization";

    [Fact]
    public async Task List_WithMembership_PostsTypedRpcArgsAndMapsRowsAndTotal()
    {
        var organizationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requests = new List<(string Method, string Path, string? Body)>();
        var repository = CreateRepository(requests, _ => Json(new
        {
            total_count = 7,
            items = new[] { UserRowJson(userId, tenantId, organizationId) }
        }));

        var (items, totalCount) = await repository.ListAsync(new UserQuery(
            tenantId, "  Ana  ", UserRole.Editor, true, 3, 2,
            new UserOrganizationMembership(organizationId, tenantId)));

        var request = Assert.Single(requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(RpcPath, request.Path);
        using var json = JsonDocument.Parse(request.Body!);
        var args = json.RootElement;
        Assert.Equal(organizationId, args.GetProperty("p_organization_id").GetGuid());
        Assert.Equal(tenantId, args.GetProperty("p_tenant_id").GetGuid());
        Assert.Equal(tenantId, args.GetProperty("p_include_unassigned_tenant_id").GetGuid());
        Assert.Equal("Ana", args.GetProperty("p_search").GetString());
        Assert.Equal("editor", args.GetProperty("p_role").GetString());
        Assert.True(args.GetProperty("p_is_active").GetBoolean());
        Assert.Equal(4, args.GetProperty("p_offset").GetInt32());
        Assert.Equal(2, args.GetProperty("p_limit").GetInt32());

        Assert.Equal(7, totalCount);
        var user = Assert.Single(items);
        Assert.Equal(userId, user.Id.Value);
        Assert.Equal("ana@example.test", user.Email);
        Assert.Equal("Ana Member", user.FullName);
        Assert.Equal(UserRole.Editor, user.Role);
        Assert.Equal(tenantId, user.TenantId);
        Assert.Equal(organizationId, user.OrganizationId);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task List_WithMembershipAndNoFilters_SendsExplicitNullsAndFirstPageOffset()
    {
        var requests = new List<(string Method, string Path, string? Body)>();
        var repository = CreateRepository(requests, _ => Json(new { total_count = 0, items = Array.Empty<object>() }));

        await repository.ListAsync(new UserQuery(
            null, "   ", null, null, 0, 25,
            new UserOrganizationMembership(Guid.NewGuid(), null)));

        using var json = JsonDocument.Parse(Assert.Single(requests).Body!);
        var args = json.RootElement;
        foreach (var name in new[] { "p_tenant_id", "p_include_unassigned_tenant_id", "p_search", "p_role", "p_is_active" })
        {
            Assert.Equal(JsonValueKind.Null, args.GetProperty(name).ValueKind);
        }

        Assert.Equal(0, args.GetProperty("p_offset").GetInt32());
        Assert.Equal(25, args.GetProperty("p_limit").GetInt32());
    }

    [Fact]
    public async Task List_WithMembershipOutOfRangePage_ReturnsEmptyItemsWithAccurateTotal()
    {
        var repository = CreateRepository([], _ => Json(new { total_count = 12, items = Array.Empty<object>() }));

        var (items, totalCount) = await repository.ListAsync(new UserQuery(
            null, null, null, null, 9, 10,
            new UserOrganizationMembership(Guid.NewGuid(), null)));

        Assert.Empty(items);
        Assert.Equal(12, totalCount);
    }

    [Fact]
    public async Task List_WithMembershipRpcFailure_Throws()
    {
        var repository = CreateRepository([], _ => new HttpResponseMessage(HttpStatusCode.BadRequest));

        await Assert.ThrowsAsync<HttpRequestException>(() => repository.ListAsync(new UserQuery(
            null, null, null, null, 1, 10,
            new UserOrganizationMembership(Guid.NewGuid(), null))));
    }

    [Fact]
    public async Task List_WithMembershipNullRpcResult_Throws()
    {
        var repository = CreateRepository([], _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ListAsync(new UserQuery(
            null, null, null, null, 1, 10,
            new UserOrganizationMembership(Guid.NewGuid(), null))));
    }

    [Fact]
    public async Task List_WithoutMembership_KeepsTenantRestQueryAndContentRangeTotal()
    {
        var tenantId = Guid.NewGuid();
        var requests = new List<(string Method, string Path, string? Body)>();
        var repository = CreateRepository(requests, _ =>
        {
            var response = Json(new[] { UserRowJson(Guid.NewGuid(), tenantId, null) });
            response.Content.Headers.TryAddWithoutValidation("Content-Range", "0-0/5");
            return response;
        });

        var (items, totalCount) = await repository.ListAsync(new UserQuery(tenantId, null, null, null, 1, 10));

        var request = Assert.Single(requests);
        Assert.Equal("GET", request.Method);
        Assert.StartsWith("/rest/v1/users?", request.Path, StringComparison.Ordinal);
        Assert.Contains($"tenant_id=eq.{tenantId}", request.Path, StringComparison.Ordinal);
        Assert.Contains("offset=0&limit=10", request.Path, StringComparison.Ordinal);
        Assert.Single(items);
        Assert.Equal(5, totalCount);
    }

    [Fact]
    public async Task List_WithExcludeRole_AddsNeqFilterAlongsideRoleEqFilter()
    {
        var tenantId = Guid.NewGuid();
        var requests = new List<(string Method, string Path, string? Body)>();
        var repository = CreateRepository(requests, _ => Json(Array.Empty<object>()));

        await repository.ListAsync(new UserQuery(
            tenantId, null, UserRole.Editor, null, 1, 10, ExcludeRole: UserRole.SystemAdmin));

        var request = Assert.Single(requests);
        Assert.Equal("GET", request.Method);
        Assert.Contains($"tenant_id=eq.{tenantId}", request.Path, StringComparison.Ordinal);
        Assert.Contains("role=eq.editor", request.Path, StringComparison.Ordinal);
        Assert.Contains("role=neq.system_admin", request.Path, StringComparison.Ordinal);
        Assert.Contains("offset=0&limit=10", request.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_WithExcludeRoleAndNoTenant_AddsNeqFilterToGlobalQuery()
    {
        var requests = new List<(string Method, string Path, string? Body)>();
        var repository = CreateRepository(requests, _ => Json(Array.Empty<object>()));

        await repository.ListAsync(new UserQuery(
            null, null, null, null, 1, 10, ExcludeRole: UserRole.SystemAdmin));

        var request = Assert.Single(requests);
        Assert.DoesNotContain("tenant_id=", request.Path, StringComparison.Ordinal);
        Assert.DoesNotContain("role=eq.", request.Path, StringComparison.Ordinal);
        Assert.Contains("role=neq.system_admin", request.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_WithoutExcludeRole_DoesNotAddNeqFilter()
    {
        var requests = new List<(string Method, string Path, string? Body)>();
        var repository = CreateRepository(requests, _ => Json(Array.Empty<object>()));

        await repository.ListAsync(new UserQuery(null, null, UserRole.Admin, null, 1, 10));

        var request = Assert.Single(requests);
        Assert.Contains("role=eq.admin", request.Path, StringComparison.Ordinal);
        Assert.DoesNotContain("neq.", request.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_WithExcludeRoleAndMembership_FailsClosedWithoutCallingProvider()
    {
        var requests = new List<(string Method, string Path, string? Body)>();
        var repository = CreateRepository(requests, _ => Json(new { total_count = 0, items = Array.Empty<object>() }));

        await Assert.ThrowsAsync<NotSupportedException>(() => repository.ListAsync(new UserQuery(
            null, null, null, null, 1, 10,
            new UserOrganizationMembership(Guid.NewGuid(), null),
            UserRole.SystemAdmin)));

        Assert.Empty(requests);
    }

    private static UserRepository CreateRepository(
        List<(string Method, string Path, string? Body)> requests,
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new RecordingHandler(async request =>
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));
            return respond(request);
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        return new UserRepository(new ClientFactory(client), new SupabaseOperationContext());
    }

    private static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    };

    private static Dictionary<string, object?> UserRowJson(Guid id, Guid? tenantId, Guid? organizationId) => new()
    {
        ["id"] = id,
        ["email"] = "ana@example.test",
        ["full_name"] = "Ana Member",
        ["phone"] = null,
        ["birth_date"] = null,
        ["password_hash"] = "not-a-real-hash",
        ["role"] = "editor",
        ["role_id"] = "role004",
        ["tenant_id"] = tenantId,
        ["organization_id"] = organizationId,
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

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request);
    }
}
