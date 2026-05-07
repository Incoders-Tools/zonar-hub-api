using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.TenantSports;
using ZonarHub.Application.Features.TenantSports.Get;
using ZonarHub.Application.Features.TenantSports.Set;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ZonarHub.ApiService.Endpoints.Admin.System.Tenants;

public static class TenantSportsEndpointsExtensions
{
    private const string Tag = "Admin.Tenants";

    public static IEndpointRouteBuilder MapTenantSportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/tenants/{tenantId:guid}/sports").WithTags(Tag);

        group.MapGet("/", GetAsync)
            .WithName("GetTenantSports")
            .WithSummary("Get all sports with enabled flag for a tenant")
            .Produces<TenantSportsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/", SetAsync)
            .WithName("SetTenantSports")
            .WithSummary("Set which sports are enabled for a tenant")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetAsync(
        Guid tenantId,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var requiredTenantId = httpContext.GetTenantId();
        var result = await sender.Send(new GetTenantSportsQuery(tenantId, requiredTenantId), cancellationToken);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> SetAsync(
        Guid tenantId,
        [FromBody] SetTenantSportsRequest body,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var requiredTenantId = httpContext.GetTenantId();
        var command = new SetTenantSportsCommand(tenantId, body.EnabledSportIds, requiredTenantId);
        var result = await sender.Send(command, cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

public sealed record SetTenantSportsRequest(IEnumerable<Guid> EnabledSportIds);
