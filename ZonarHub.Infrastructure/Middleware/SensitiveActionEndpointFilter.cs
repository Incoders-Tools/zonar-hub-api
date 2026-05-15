using Microsoft.AspNetCore.Http;
using ZonarHub.Infrastructure.Auth.Impersonation;

namespace ZonarHub.Infrastructure.Middleware;

/// <summary>
/// Endpoint filter (IEndpointFilter) registered globally that blocks sensitive routes
/// when the request carries an impersonation token.
///
/// Also validates that the impersonation session is not revoked via
/// <see cref="ImpersonationContext"/>. Session revocation check (against DB) is
/// done by the middleware that populates <see cref="ImpersonationContext"/> — this
/// filter only reads the already-populated context.
///
/// Returns 403 with problem JSON <c>impersonation.forbidden</c> when blocked.
/// Whitelists the stop endpoint so the sysadmin can always exit (design §3.4).
/// Satisfies: REQ-IMP-031, REQ-IMP-034, REQ-IMP-035, design §3.4, §3.5, §2.3.
/// </summary>
public sealed class SensitiveActionEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpCtx = context.HttpContext;
        var principal = httpCtx.User;

        // Build impersonation context from the current principal.
        var impCtx = ImpersonationContext.FromPrincipal(principal);

        // Only apply restrictions when the request is under an impersonation token.
        if (!impCtx.IsImpersonating)
        {
            return await next(context);
        }

        // If impersonation context is malformed, reject immediately.
        if (!impCtx.IsValid)
        {
            return TypedResults.Problem(
                title: "Invalid impersonation token",
                detail: "admin.impersonation.errors.tokenMalformed",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["code"] = "impersonation.tokenMalformed" });
        }

        var method = httpCtx.Request.Method;
        var path = httpCtx.Request.Path.Value ?? string.Empty;

        var decision = SensitiveRouteRegistry.Decide(method, path);

        if (decision == RouteDecision.Blocked)
        {
            return TypedResults.Problem(
                title: "Forbidden under impersonation",
                detail: "admin.impersonation.errors.sensitiveBlocked",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["code"] = "impersonation.forbidden" });
        }

        if (decision == RouteDecision.ReadOnly &&
            !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(
                title: "Read-only under impersonation",
                detail: "admin.impersonation.errors.sensitiveBlocked",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["code"] = "impersonation.forbidden" });
        }

        return await next(context);
    }
}
