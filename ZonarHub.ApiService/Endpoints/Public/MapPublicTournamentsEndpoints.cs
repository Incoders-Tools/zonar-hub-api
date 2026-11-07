using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.PublicTournaments;
using ZonarHub.Application.Features.PublicTournaments.GetAll;
using ZonarHub.Application.Features.PublicTournaments.GetBySlug;

namespace ZonarHub.ApiService.Endpoints.Public;

/// <summary>
/// Anonymous, read-only tournament showcase. This group is explicitly opened (<c>AllowAnonymous</c>) and maps GET
/// routes only; it never exposes internal ids or admin-only fields.
/// </summary>
public static class PublicTournamentsEndpointsExtensions
{
    private const string Tag = "Public.Tournaments";
    private const string RoutePrefix = "/api/public/tournaments";

    /// <summary>Short shared cache: the showcase tolerates a minute of staleness.</summary>
    private const string CacheControl = "public, max-age=60";

    public static IEndpointRouteBuilder MapPublicTournamentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .AllowAnonymous();

        group.MapGet("/", GetAllAsync)
            .WithName("GetPublicTournaments")
            .WithSummary("List public tournaments")
            .WithDescription("Anonymous. Returns tournaments of every organization that are public, active and not cancelled, ordered by start date ascending. The optional limit defaults to and is capped at 100. organization is null unless the organization is active and enabled its public profile; its links object only carries non-empty links. Responses are cacheable for 60 seconds.")
            .Produces<PublicTournamentListResponse>(StatusCodes.Status200OK);

        group.MapGet("/{slug}", GetBySlugAsync)
            .WithName("GetPublicTournamentBySlug")
            .WithSummary("Get a public tournament by its share slug")
            .WithDescription("Anonymous. Unknown, malformed, non-public, inactive and cancelled slugs all return 404. Responses are cacheable for 60 seconds.")
            .Produces<PublicTournamentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    internal static async Task<IResult> GetAllAsync(
        [FromQuery] int? limit,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPublicTournamentsQuery(limit), cancellationToken);
        return result.Match(items => Cached(httpContext, TypedResults.Ok(new PublicTournamentListResponse(items))));
    }

    internal static async Task<IResult> GetBySlugAsync(
        string slug,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPublicTournamentBySlugQuery(slug), cancellationToken);
        return result.Match(item => Cached(httpContext, TypedResults.Ok(item)));
    }

    private static IResult Cached(HttpContext httpContext, IResult result)
    {
        httpContext.Response.Headers.CacheControl = CacheControl;
        return result;
    }
}
