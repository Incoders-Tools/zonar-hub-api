using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.AdminDashboard;
using ZonarHub.Application.Features.AdminDashboard.GetSummary;

namespace ZonarHub.ApiService.Endpoints.Admin.Dashboard;

public static class AdminDashboardEndpointsExtensions
{
    private const string Tag = "Admin.Dashboard";
    private const string RoutePrefix = "/api/admin/dashboard";

    public static IEndpointRouteBuilder MapAdminDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization("AdminOrAbove");

        group.MapGet("/summary", GetSummaryAsync)
            .WithName("GetAdminDashboardSummary")
            .WithSummary("Get admin dashboard summary")
            .WithDescription("Returns organization-scoped dashboard metrics for admin cards and setup checklist.")
            .Produces<AdminDashboardSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetSummaryAsync(
        [FromQuery] Guid organizationId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetAdminDashboardSummaryQuery(organizationId), cancellationToken);
        return result.Match(summary => (IResult)TypedResults.Ok(summary));
    }
}
