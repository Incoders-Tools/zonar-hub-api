using System.Security.Claims;

namespace ZonarHub.ApiService.Endpoints.Common;

/// <summary>
/// Extracts the current tenant identifier from the <c>X-Tenant-Id</c> request header,
/// with fallback to the <c>tenantId</c> JWT claim.
/// </summary>
internal static class TenantHeaders
{
    public const string HeaderName = "X-Tenant-Id";

    public static Guid? GetTenantId(this HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var value) &&
            Guid.TryParse(value.FirstOrDefault(), out var fromHeader))
            return fromHeader;

        var claim = context.User?.FindFirstValue("tenantId");
        if (claim is not null && Guid.TryParse(claim, out var fromJwt))
            return fromJwt;

        return null;
    }
}
