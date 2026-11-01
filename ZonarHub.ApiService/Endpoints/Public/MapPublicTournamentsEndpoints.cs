using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.PublicTournaments;
using ZonarHub.Application.Features.PublicTournaments.GetAll;
using ZonarHub.Application.Features.PublicTournaments.GetBySlug;
using ZonarHub.Application.Features.PublicTournaments.GetDraw;
using ZonarHub.Application.Features.TournamentDraws;

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

    /// <summary>The draw is polled every ~30 seconds by open pages (S5), so it is cached for half of that.</summary>
    private const string DrawCacheControl = "public, max-age=15";

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

        group.MapGet("/{slug}/draw", GetDrawAsync)
            .WithName("GetPublicTournamentDraw")
            .WithSummary("Get the published draw of a public tournament")
            .WithDescription("Anonymous. Returns the tournament's draw resolved with its results: winners advanced into their next match, byes advanced, group and round robin standings computed. The tournament must pass the same filters as the public detail (public, active, not cancelled); otherwise, and for malformed slugs, the response is 404 with code public_tournaments.not_found. A tournament without a published draw (none, or a private draft) returns 404 with code public_draws.not_found. The public draw carries participant display names and members, draw-local participant and match ids and court names only: no separation keys or conflicts, court ids, submitted request or owner ids. Responses are cacheable for 15 seconds.")
            .Produces<TournamentDrawResponse>(StatusCodes.Status200OK)
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

    internal static async Task<IResult> GetDrawAsync(
        string slug,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPublicTournamentDrawQuery(slug), cancellationToken);
        return result.Match(draw => Cached(httpContext, TypedResults.Ok(draw), DrawCacheControl));
    }

    private static IResult Cached(HttpContext httpContext, IResult result, string cacheControl = CacheControl)
    {
        httpContext.Response.Headers.CacheControl = cacheControl;
        return result;
    }
}
