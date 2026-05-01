using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Auth;

internal sealed class HttpContextCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; }
    public Guid? UserId { get; }
    public string? Email { get; }

    public HttpContextCurrentUser(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User;
        IsAuthenticated = principal?.Identity?.IsAuthenticated ?? false;

        var sub = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? principal?.FindFirstValue(JwtClaimNames.Sub);
        UserId = Guid.TryParse(sub, out var id) ? id : null;

        Email = principal?.FindFirstValue(ClaimTypes.Email)
             ?? principal?.FindFirstValue(JwtClaimNames.Email);
    }

    private static class JwtClaimNames
    {
        public const string Sub = "sub";
        public const string Email = "email";
    }
}
