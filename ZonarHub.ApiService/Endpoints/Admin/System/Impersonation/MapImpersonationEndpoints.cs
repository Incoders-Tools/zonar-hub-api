using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.Impersonation.Health;
using ZonarHub.Application.Features.Impersonation.Start;
using ZonarHub.Application.Features.Impersonation.Stop;

namespace ZonarHub.ApiService.Endpoints.Admin.System.Impersonation;

public static class ImpersonationEndpointsExtensions
{
    private const string Tag = "Impersonation";
    private const string RoutePrefix = "/api/admin/impersonation";

    public static IEndpointRouteBuilder MapImpersonationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix).WithTags(Tag);

        group.MapGet("/health", HealthAsync)
            .WithName("ImpersonationHealth")
            .WithSummary("Reports whether the impersonation feature is enabled.")
            .AllowAnonymous();

        group.MapPost("/start", StartAsync)
            .WithName("ImpersonationStart")
            .WithSummary("Starts a new impersonation session (sysadmin only).")
            .RequireAuthorization();

        group.MapPost("/stop", StopAsync)
            .WithName("ImpersonationStop")
            .WithSummary("Revokes the current impersonation session.")
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HealthAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetImpersonationHealthQuery(), cancellationToken);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> StartAsync(
        [FromBody] StartImpersonationRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new StartImpersonationCommand(body.UserId, body.Reason),
            cancellationToken);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> StopAsync(
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var sessionIdRaw = httpContext.User.FindFirst("imp_session_id")?.Value;
        var sessionId = Guid.TryParse(sessionIdRaw, out var parsed) ? parsed : Guid.Empty;

        var result = await sender.Send(
            new StopImpersonationCommand(sessionId),
            cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

public sealed record StartImpersonationRequest(Guid UserId, string? Reason);
