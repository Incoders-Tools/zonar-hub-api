using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.OrganizationSports;
using ZonarHub.Application.Features.OrganizationSports.Get;
using ZonarHub.Application.Features.OrganizationSports.Set;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ZonarHub.ApiService.Endpoints.Admin.System.Organizations;

public static class OrganizationSportsEndpointsExtensions
{
    private const string Tag = "Admin.Organizations";

    public static IEndpointRouteBuilder MapOrganizationSportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/organizations/{organizationId:guid}/sports").WithTags(Tag);

        group.MapGet("/", GetAsync)
            .WithName("GetOrganizationSports")
            .WithSummary("Get all sports with enabled flag for an organization")
            .Produces<OrganizationSportsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/", SetAsync)
            .WithName("SetOrganizationSports")
            .WithSummary("Set which sports are enabled for an organization")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        // TODO: .RequireAuthorization("Admin")

        return app;
    }

    private static async Task<IResult> GetAsync(
        Guid organizationId,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var tenantId = httpContext.GetTenantId();
        var result = await sender.Send(new GetOrganizationSportsQuery(organizationId, tenantId), cancellationToken);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> SetAsync(
        Guid organizationId,
        [FromBody] SetOrganizationSportsRequest body,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var tenantId = httpContext.GetTenantId();
        var command = new SetOrganizationSportsCommand(organizationId, body.EnabledSportIds, tenantId);
        var result = await sender.Send(command, cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

public sealed record SetOrganizationSportsRequest(IEnumerable<Guid> EnabledSportIds);
