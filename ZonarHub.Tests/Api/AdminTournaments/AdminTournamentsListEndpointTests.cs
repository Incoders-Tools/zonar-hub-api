using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.AdminTournaments.GetAll;
using ZonarHub.Domain.Users;
using ZonarHub.Tests.Application.AdminTournaments;

namespace ZonarHub.Tests.Api.AdminTournaments;

public class AdminTournamentsListEndpointTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TenantAdmin_AnotherTenantsOrganization_ReturnsForbiddenProblemWithCode()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var caller = await h.Users.SeedUserAsync(UserRole.Admin, Guid.NewGuid(), "admin@zonarhub.dev", "Admin");
        var foreignOrg = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        h.Users.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await AdminTournamentsEndpointsExtensions.GetAllAsync(
            foreignOrg.Id.Value, new ListHandlerSender(h.List), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal(AdminTournamentErrors.Forbidden.Code, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(AdminTournamentErrors.Forbidden.MessageKey, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task TenantAdmin_OwnTenantOrganization_ReturnsOkList()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var tenant = Guid.NewGuid();
        var caller = await h.Users.SeedUserAsync(UserRole.Admin, tenant, "admin@zonarhub.dev", "Admin");
        var org = await h.Users.SeedOrganizationAsync(tenant, "Org A");
        h.Tournaments.Seed(org.Id.Value, "Open A");
        h.Users.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await AdminTournamentsEndpointsExtensions.GetAllAsync(
            org.Id.Value, new ListHandlerSender(h.List), CancellationToken.None);

        var ok = Assert.IsType<Ok<TournamentListResponse>>(result);
        Assert.Single(ok.Value!.Items);
    }

    [Fact]
    public async Task EmptyOrganizationId_ReturnsValidationProblem()
    {
        var h = new AdminTournamentsListTestHarness(Now);

        var result = await AdminTournamentsEndpointsExtensions.GetAllAsync(
            Guid.Empty, new ListHandlerSender(h.List), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(AdminTournamentErrors.OrganizationRequired.Code, problem.ProblemDetails.Extensions["code"]);
    }

    /// <summary>
    /// Routes the list query to the real handler so the endpoint maps genuine handler results.
    /// </summary>
    private sealed class ListHandlerSender(GetTournamentsHandler handler) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var query = Assert.IsType<GetTournamentsQuery>(request);
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
