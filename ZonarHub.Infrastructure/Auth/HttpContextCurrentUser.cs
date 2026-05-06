using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Auth;

internal sealed class HttpContextCurrentUser : ICurrentUser
{
    private const string OrganizationHeaderName = "X-Organization-Id";

    public bool IsAuthenticated { get; }
    public Guid? UserId { get; }
    public string? Email { get; }
    public Guid? OrganizationId { get; }

    public HttpContextCurrentUser(IHttpContextAccessor accessor)
    {
        var httpContext = accessor.HttpContext;
        var principal = accessor.HttpContext?.User;
        IsAuthenticated = principal?.Identity?.IsAuthenticated ?? false;

        var sub = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? principal?.FindFirstValue(JwtClaimNames.Sub);
        UserId = Guid.TryParse(sub, out var id) ? id : null;

        Email = principal?.FindFirstValue(ClaimTypes.Email)
             ?? principal?.FindFirstValue(JwtClaimNames.Email);

        var organizationHeader = httpContext?.Request.Headers[OrganizationHeaderName].FirstOrDefault();
        if (organizationHeader is not null && Guid.TryParse(organizationHeader, out var fromHeader))
        {
            OrganizationId = fromHeader;
            return;
        }

        var organizationClaim = principal?.FindFirstValue("organizationId");
        OrganizationId = organizationClaim is not null && Guid.TryParse(organizationClaim, out var fromClaim)
            ? fromClaim
            : null;
    }

    private static class JwtClaimNames
    {
        public const string Sub = "sub";
        public const string Email = "email";
    }
}
