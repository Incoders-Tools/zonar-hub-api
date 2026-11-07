using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ZonarHub.ApiService.Endpoints.Admin.System.Permissions;
using ZonarHub.Application.Features.AdminPermissions;
using ZonarHub.Application.Features.AdminPermissions.GetUserPermissions;
using ZonarHub.Domain.Users;
using ZonarHub.Tests.Application.AdminPermissions;
using ZonarHub.Tests.Application.AdminUsers;

namespace ZonarHub.Tests.Api.AdminPermissions;

public class GetAdminUserPermissionsEndpointTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TenantAdmin_ForeignTenantTarget_ReturnsForbiddenProblemWithCode()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.Admin, Guid.NewGuid(), "admin@zonarhub.dev", "Admin");
        var target = await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "viewer@zonarhub.dev", "Viewer");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await GetAsync(h, target.Id.Value);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("Forbidden", problem.ProblemDetails.Title);
        Assert.Equal(AdminPermissionErrors.Forbidden.Code, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(AdminPermissionErrors.Forbidden.MessageKey, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task UnknownTarget_ReturnsNotFoundProblem()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await GetAsync(h, Guid.NewGuid());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal(AdminPermissionErrors.UserNotFound.Code, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task TenantAdmin_ReturnsOkMatrixWithoutForeignTenantOrganizations()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var target = await GetAdminUserPermissionsScopeTests.SeedTargetAsync(
            h,
            tenantId,
            primaryOrganizationId: foreignOrg.Id.Value,
            assignedOrganizationIds: [ownOrg.Id.Value, foreignOrg.Id.Value]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await GetAsync(h, target.Id.Value);

        var ok = Assert.IsType<Ok<AdminUserPermissionsResponse>>(result);
        Assert.Equal(
            new[] { ownOrg.Id.Value },
            ok.Value!.PermissionsByOrganization.Select(item => item.OrganizationId).ToArray());
    }

    private static Task<IResult> GetAsync(AdminUsersTestHarness h, Guid userId) =>
        AdminPermissionsEndpointsExtensions.GetUserPermissionsAsync(
            userId,
            new GetAdminUserPermissionsSender(GetAdminUserPermissionsScopeTests.Handler(h)),
            CancellationToken.None);

    /// <summary>
    /// Routes the query to the real handler so the endpoint maps genuine handler results.
    /// </summary>
    private sealed class GetAdminUserPermissionsSender(GetAdminUserPermissionsHandler handler) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var query = Assert.IsType<GetAdminUserPermissionsQuery>(request);
            object result = await handler.Handle(query, cancellationToken);
            return (TResponse)result;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
