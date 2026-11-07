using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ZonarHub.ApiService.Endpoints.Admin.System.Users;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.AdminUsers;
using ZonarHub.Application.Features.AdminUsers.GetAll;
using ZonarHub.Domain.Users;
using ZonarHub.Tests.Application.AdminUsers;

namespace ZonarHub.Tests.Api.AdminUsers;

public class AdminUsersListEndpointTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TenantAdmin_AllScope_ReturnsForbiddenProblemWithCode()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.Admin, Guid.NewGuid(), "admin@zonarhub.dev", "Admin");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await ListAsync(h, AdminUserListScopes.All, organizationId: null);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal(AdminUserErrors.Forbidden.Code, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(AdminUserErrors.Forbidden.MessageKey, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task SystemAdmin_AllScope_ReturnsOkPage()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "viewer@zonarhub.dev", "Viewer");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await ListAsync(h, AdminUserListScopes.All, organizationId: null);

        var ok = Assert.IsType<Ok<PageResult<AdminUserResponse>>>(result);
        Assert.Equal(2, ok.Value!.TotalCount);
    }

    [Fact]
    public async Task InvalidScope_ReturnsValidationProblemWithCode()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await ListAsync(h, "global", organizationId: null);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(AdminUserErrors.ListScopeInvalid.Code, problem.ProblemDetails.Extensions["code"]);
    }

    private static Task<IResult> ListAsync(AdminUsersTestHarness h, string? scope, Guid? organizationId) =>
        AdminUsersEndpointsExtensions.ListAsync(
            new ListHandlerSender(h.List),
            CancellationToken.None,
            scope: scope,
            organizationId: organizationId);

    /// <summary>
    /// Routes the list query to the real handler so the endpoint maps genuine handler results.
    /// </summary>
    private sealed class ListHandlerSender(GetAdminUsersHandler handler) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var query = Assert.IsType<GetAdminUsersQuery>(request);
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
