using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments;
using ZonarHub.ApiService.Endpoints.Public;
using ZonarHub.Application.Features.PublicTournaments;
using ZonarHub.Application.Features.PublicTournaments.GetAll;
using ZonarHub.Application.Features.PublicTournaments.GetBySlug;
using ZonarHub.Tests.Application.PublicTournaments;
using static ZonarHub.Tests.Application.PublicTournaments.PublicTournamentsTestHarness;

namespace ZonarHub.Tests.Api.PublicTournaments;

public class PublicTournamentsEndpointTests
{
    private const string CacheControl = "public, max-age=60";

    [Fact]
    public async Task GetAll_ReturnsTheShowcaseListWithAShortPublicCache()
    {
        var h = new PublicTournamentsTestHarness();
        h.Tournaments.Items.Add(Record("first-aaaaaa", new DateOnly(2026, 11, 1)));
        h.Tournaments.Items.Add(Record("second-bbbbbb", new DateOnly(2026, 12, 1)));
        var context = new DefaultHttpContext();

        var result = await PublicTournamentsEndpointsExtensions.GetAllAsync(20, context, new HandlerSender(h), CancellationToken.None);

        var ok = Assert.IsType<Ok<PublicTournamentListResponse>>(result);
        Assert.Equal(["first-aaaaaa", "second-bbbbbb"], ok.Value!.Items.Select(t => t.Slug));
        Assert.Equal([20], h.Tournaments.ListLimits);
        Assert.Equal(CacheControl, context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task GetBySlug_ShowcaseTournament_ReturnsOkWithAShortPublicCache()
    {
        var h = new PublicTournamentsTestHarness();
        h.Tournaments.Items.Add(Record());
        var context = new DefaultHttpContext();

        var result = await PublicTournamentsEndpointsExtensions.GetBySlugAsync(
            "open-norte-abc123", context, new HandlerSender(h), CancellationToken.None);

        var ok = Assert.IsType<Ok<PublicTournamentResponse>>(result);
        Assert.Equal("open-norte-abc123", ok.Value!.Slug);
        Assert.Equal(CacheControl, context.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData("hidden-cup-abc123")]
    [InlineData("NOT A SLUG")]
    public async Task GetBySlug_UnknownOrMalformed_ReturnsNotFoundProblem(string slug)
    {
        var h = new PublicTournamentsTestHarness();
        var context = new DefaultHttpContext();

        var result = await PublicTournamentsEndpointsExtensions.GetBySlugAsync(slug, context, new HandlerSender(h), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal(PublicTournamentErrors.NotFound.Code, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(PublicTournamentErrors.NotFound.MessageKey, problem.ProblemDetails.Detail);
        Assert.True(string.IsNullOrEmpty(context.Response.Headers.CacheControl.ToString()));
    }

    [Fact]
    public void PublicEndpoints_AreGetOnlyAndAnonymous()
    {
        var endpoints = MappedEndpoints(app => app.MapPublicTournamentsEndpoints());

        Assert.Equal(
            ["/api/public/tournaments/", "/api/public/tournaments/{slug}"],
            endpoints.Select(e => e.RoutePattern.RawText).Order());
        foreach (var endpoint in endpoints)
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods;
            Assert.Equal(["GET"], methods);
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        }
    }

    [Fact]
    public void AdminTournamentEndpoints_StillRequireTheAdminPolicy()
    {
        var endpoints = MappedEndpoints(app => app.MapAdminTournamentsEndpoints());

        Assert.Equal(5, endpoints.Count);
        foreach (var endpoint in endpoints)
        {
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), a => a.Policy == "AdminOrAbove");
        }
    }

    private static List<RouteEndpoint> MappedEndpoints(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<ISender>(new HandlerSender(new PublicTournamentsTestHarness()));
        var app = builder.Build();
        map(app);
        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    /// <summary>Routes the public queries to the real handlers.</summary>
    private sealed class HandlerSender(PublicTournamentsTestHarness h) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                GetPublicTournamentsQuery list => await h.List.Handle(list, cancellationToken),
                GetPublicTournamentBySlugQuery bySlug => await h.BySlug.Handle(bySlug, cancellationToken),
                _ => throw new NotSupportedException(),
            };
            return (TResponse)result;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
