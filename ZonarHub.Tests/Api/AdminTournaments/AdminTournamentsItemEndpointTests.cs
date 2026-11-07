using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.AdminTournaments.Create;
using ZonarHub.Application.Features.AdminTournaments.Delete;
using ZonarHub.Application.Features.AdminTournaments.GetById;
using ZonarHub.Application.Features.AdminTournaments.Update;
using ZonarHub.Domain.Users;
using ZonarHub.Tests.Application.AdminTournaments;

namespace ZonarHub.Tests.Api.AdminTournaments;

public class AdminTournamentsItemEndpointTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetById_AnotherTenantsTournament_ReturnsForbiddenProblemWithCode()
    {
        var (h, tournament) = await ForeignTournamentAsync();

        var result = await AdminTournamentsEndpointsExtensions.GetByIdAsync(
            tournament.Id, new HandlerSender(h), CancellationToken.None);

        AssertForbidden(result);
    }

    [Fact]
    public async Task GetById_UnknownTournament_ReturnsNotFoundProblem()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        await AdminTournamentsItemAccessTests.AuthenticateAsync(h, UserRole.Admin, Guid.NewGuid());

        var result = await AdminTournamentsEndpointsExtensions.GetByIdAsync(
            Guid.NewGuid(), new HandlerSender(h), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal(AdminTournamentErrors.NotFound.Code, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task GetById_OwnTenantTournament_ReturnsOk()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.Users.SeedOrganizationAsync(tenant, "Org A");
        var tournament = h.Tournaments.Seed(org.Id.Value, "Open A");
        await AdminTournamentsItemAccessTests.AuthenticateAsync(h, UserRole.Admin, tenant);

        var result = await AdminTournamentsEndpointsExtensions.GetByIdAsync(
            tournament.Id, new HandlerSender(h), CancellationToken.None);

        Assert.Equal(tournament.Id, Assert.IsType<Ok<TournamentResponse>>(result).Value!.Id);
    }

    [Fact]
    public async Task Create_AnotherTenantsOrganization_ReturnsForbiddenProblemWithCode()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var foreignOrg = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        await AdminTournamentsItemAccessTests.AuthenticateAsync(h, UserRole.Admin, Guid.NewGuid());

        var result = await AdminTournamentsEndpointsExtensions.CreateAsync(
            CreateRequest(foreignOrg.Id.Value), new HandlerSender(h), CancellationToken.None);

        AssertForbidden(result);
        Assert.Empty(h.Tournaments.Added);
    }

    [Fact]
    public async Task Create_OwnTenantOrganization_ReturnsCreated()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.Users.SeedOrganizationAsync(tenant, "Org A");
        await AdminTournamentsItemAccessTests.AuthenticateAsync(h, UserRole.Admin, tenant);

        var result = await AdminTournamentsEndpointsExtensions.CreateAsync(
            CreateRequest(org.Id.Value), new HandlerSender(h), CancellationToken.None);

        var created = Assert.IsType<Created<TournamentResponse>>(result);
        Assert.Equal($"/api/admin/tournaments/{created.Value!.Id}", created.Location);
    }

    [Fact]
    public async Task Create_EmptyOrganizationId_ReturnsValidationProblem()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        await AdminTournamentsItemAccessTests.AuthenticateAsync(h, UserRole.SystemAdmin, tenantId: null);

        var result = await AdminTournamentsEndpointsExtensions.CreateAsync(
            CreateRequest(Guid.Empty), new HandlerSender(h), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(AdminTournamentErrors.OrganizationRequired.Code, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Update_AnotherTenantsTournament_ReturnsForbiddenProblemWithCode()
    {
        var (h, tournament) = await ForeignTournamentAsync();

        var result = await AdminTournamentsEndpointsExtensions.UpdateAsync(
            tournament.Id, UpdateRequest(), new HandlerSender(h), CancellationToken.None);

        AssertForbidden(result);
        Assert.Empty(h.Tournaments.Updated);
    }

    [Fact]
    public async Task Update_OwnTenantTournament_ReturnsOk()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.Users.SeedOrganizationAsync(tenant, "Org A");
        var tournament = h.Tournaments.Seed(org.Id.Value, "Open A");
        await AdminTournamentsItemAccessTests.AuthenticateAsync(h, UserRole.Admin, tenant);

        var result = await AdminTournamentsEndpointsExtensions.UpdateAsync(
            tournament.Id, UpdateRequest(), new HandlerSender(h), CancellationToken.None);

        Assert.Equal("Renamed Open", Assert.IsType<Ok<TournamentResponse>>(result).Value!.Name);
    }

    [Fact]
    public async Task Delete_AnotherTenantsTournament_ReturnsForbiddenProblemWithCode()
    {
        var (h, tournament) = await ForeignTournamentAsync();

        var result = await AdminTournamentsEndpointsExtensions.DeleteAsync(
            tournament.Id, new HandlerSender(h), CancellationToken.None);

        AssertForbidden(result);
        Assert.Empty(h.Tournaments.DeletedIds);
    }

    [Fact]
    public async Task Delete_OwnTenantTournament_ReturnsNoContent()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.Users.SeedOrganizationAsync(tenant, "Org A");
        var tournament = h.Tournaments.Seed(org.Id.Value, "Open A");
        await AdminTournamentsItemAccessTests.AuthenticateAsync(h, UserRole.Admin, tenant);

        var result = await AdminTournamentsEndpointsExtensions.DeleteAsync(
            tournament.Id, new HandlerSender(h), CancellationToken.None);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task Delete_UnknownTournament_ReturnsNotFoundProblem()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        await AdminTournamentsItemAccessTests.AuthenticateAsync(h, UserRole.SystemAdmin, tenantId: null);

        var result = await AdminTournamentsEndpointsExtensions.DeleteAsync(
            Guid.NewGuid(), new HandlerSender(h), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    private static async Task<(AdminTournamentsListTestHarness Harness, TournamentAdminDto Tournament)> ForeignTournamentAsync()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var foreignOrg = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var tournament = h.Tournaments.Seed(foreignOrg.Id.Value, "Foreign Open");
        await AdminTournamentsItemAccessTests.AuthenticateAsync(h, UserRole.Admin, Guid.NewGuid());
        return (h, tournament);
    }

    private static void AssertForbidden(IResult result)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal(AdminTournamentErrors.Forbidden.Code, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(AdminTournamentErrors.Forbidden.MessageKey, problem.ProblemDetails.Detail);
    }

    private static CreateTournamentRequest CreateRequest(Guid organizationId) => new(
        organizationId, "Open Cup", null, null, Guid.NewGuid(), null, null, null, null, null, null,
        new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2), null, null, null, null, null, null, null,
        null, null, null, null, null, true, null);

    private static UpdateTournamentRequest UpdateRequest() => new(
        "Renamed Open", null, null, Guid.NewGuid(), null, null, null, null, null, null,
        new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2), null, null, null, null, null, null, null,
        null, null, null, null, null, true, null);

    /// <summary>
    /// Routes each tournament request to the matching real handler so endpoints map genuine handler results.
    /// </summary>
    private sealed class HandlerSender(AdminTournamentsListTestHarness h) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                GetTournamentByIdQuery query => await h.GetById.Handle(query, cancellationToken),
                CreateTournamentCommand command => await h.Create.Handle(command, cancellationToken),
                UpdateTournamentCommand command => await h.Update.Handle(command, cancellationToken),
                DeleteTournamentCommand command => await h.Delete.Handle(command, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name),
            };
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
