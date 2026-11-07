using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ZonarHub.ApiService.Endpoints.Admin.System.Permissions;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.AdminPermissions;
using ZonarHub.Application.Features.AdminPermissions.ListPermissionSources;
using ZonarHub.Domain.Users;
using ZonarHub.Tests.Application.AdminUsers;

namespace ZonarHub.Tests.Api.AdminPermissions;

public class ListPermissionSourcesEndpointTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task NonAdministrator_ReturnsForbiddenProblemWithCode()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "viewer@zonarhub.dev", "Viewer");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await ListAsync(h, "maria");

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal(AdminPermissionErrors.Forbidden.Code, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(AdminPermissionErrors.Forbidden.MessageKey, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task TenantAdmin_ReturnsOkPageScopedToOwnTenantWithoutSystemAdmins()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        await h.SeedUserAsync(UserRole.SystemAdmin, tenantId, "maria-root@zonarhub.dev", "Maria Root");
        var source = await h.SeedUserAsync(UserRole.Viewer, tenantId, "maria@zonarhub.dev", "Maria");
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "maria-foreign@zonarhub.dev", "Maria Foreign");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await ListAsync(h, "maria", page: 0, pageSize: 100);

        var ok = Assert.IsType<Ok<PageResult<PermissionSourceUserResponse>>>(result);
        Assert.Equal(1, ok.Value!.TotalCount);
        Assert.Equal(1, ok.Value.Page);
        Assert.Equal(ListPermissionSourcesLimits.MaxPageSize, ok.Value.PageSize);
        Assert.Equal(
            new PermissionSourceUserResponse(source.Id.Value, "Maria", "maria@zonarhub.dev", "role003", true),
            Assert.Single(ok.Value.Items));
    }

    [Fact]
    public async Task SystemAdmin_ReturnsOkPageAcrossTenants()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "maria-a@zonarhub.dev", "Maria A");
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "maria-b@zonarhub.dev", "Maria B");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await ListAsync(h, "maria");

        var ok = Assert.IsType<Ok<PageResult<PermissionSourceUserResponse>>>(result);
        Assert.Equal(2, ok.Value!.TotalCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*%")]
    [InlineData("m*")]
    [InlineData("__")]
    [InlineData("(),%")]
    public async Task WildcardOrShortSearch_ReturnsValidationProblemWithCode(string? search)
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await ListAsync(h, search);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ListPermissionSourcesErrors.SearchTooShort.Code, problem.ProblemDetails.Extensions["code"]);
    }

    private static Task<IResult> ListAsync(AdminUsersTestHarness h, string? search, int page = 1, int pageSize = 20) =>
        AdminPermissionsEndpointsExtensions.ListPermissionSourcesAsync(
            new ListPermissionSourcesSender(new ListPermissionSourcesHandler(h.CurrentUser, h.Users)),
            CancellationToken.None,
            search,
            page,
            pageSize);

    /// <summary>
    /// Routes the query to the real handler so the endpoint maps genuine handler results.
    /// </summary>
    private sealed class ListPermissionSourcesSender(ListPermissionSourcesHandler handler) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var query = Assert.IsType<ListPermissionSourcesQuery>(request);
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
