using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ZonarHub.ApiService.Security;

internal static class ApiSecurityExtensions
{
    public static IServiceCollection AddApiSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ApiSecurityOptions>()
            .Bind(configuration.GetSection(ApiSecurityOptions.SectionName))
            .Validate(options => options.GlobalPermitLimit > 0, "Security:GlobalPermitLimit must be greater than zero.")
            .Validate(options => options.AnonymousAuthPermitLimit > 0, "Security:AnonymousAuthPermitLimit must be greater than zero.")
            .Validate(options => options.PublicRegistrationCodePermitLimit > 0, "Security:PublicRegistrationCodePermitLimit must be greater than zero.")
            .Validate(options => options.WindowSeconds > 0, "Security:WindowSeconds must be greater than zero.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var security = context.RequestServices.GetRequiredService<IOptions<ApiSecurityOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    GetPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = security.GlobalPermitLimit,
                        QueueLimit = 0,
                        Window = TimeSpan.FromSeconds(security.WindowSeconds),
                    });
            });

            options.AddPolicy(ApiRateLimitPolicies.AnonymousAuth, context => PolicyPartition(ApiRateLimitPolicies.AnonymousAuth, context));
            options.AddPolicy(ApiRateLimitPolicies.PublicRegistrationCode, context => PolicyPartition(ApiRateLimitPolicies.PublicRegistrationCode, context));
        });

        return services;
    }

    /// <summary>
    /// The fixed window of a named policy for the caller (user, else remote IP): <see cref="ApiSecurityOptions.WindowSeconds"/>
    /// long with the policy's permit limit and no queue.
    /// </summary>
    internal static RateLimitPartition<string> PolicyPartition(string policy, HttpContext context)
    {
        var security = context.RequestServices.GetRequiredService<IOptions<ApiSecurityOptions>>().Value;
        var permitLimit = policy == ApiRateLimitPolicies.PublicRegistrationCode
            ? security.PublicRegistrationCodePermitLimit
            : security.AnonymousAuthPermitLimit;
        return RateLimitPartition.GetFixedWindowLimiter(
            GetPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(security.WindowSeconds),
            });
    }

    public static IApplicationBuilder UseApiSecurity(this IApplicationBuilder app)
    {
        var security = app.ApplicationServices.GetRequiredService<IOptions<ApiSecurityOptions>>().Value;
        if (security.TrustForwardedHeaders)
        {
            // Only the hop the hosting proxy appended (ForwardLimit 1); earlier values come from the client and are ignored.
            var forwarded = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                ForwardLimit = 1,
            };
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            app.UseForwardedHeaders(forwarded);
        }

        app.UseMiddleware<IpBlockListMiddleware>();
        app.UseRateLimiter();
        return app;
    }

    private static string GetPartitionKey(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            return $"user:{context.User.Identity.Name ?? context.User.FindFirst("sub")?.Value ?? "authenticated"}";
        }

        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}

internal sealed class ApiSecurityOptions
{
    public const string SectionName = "Security";

    public int GlobalPermitLimit { get; init; } = 300;

    public int AnonymousAuthPermitLimit { get; init; } = 20;

    /// <summary>Public registration code requests per caller and window; each one may send an email.</summary>
    public int PublicRegistrationCodePermitLimit { get; init; } = 10;

    public int WindowSeconds { get; init; } = 60;

    /// <summary>
    /// Use the client address the hosting proxy appends to <c>X-Forwarded-For</c> (last hop only) for rate limits and the
    /// block list. Enable only where every request reaches the API through that proxy.
    /// </summary>
    public bool TrustForwardedHeaders { get; init; }

    public string[] RejectedIpAddresses { get; init; } = [];
}

internal sealed class IpBlockListMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<ApiSecurityOptions> _options;
    private readonly ILogger<IpBlockListMiddleware> _logger;

    public IpBlockListMiddleware(
        RequestDelegate next,
        IOptionsMonitor<ApiSecurityOptions> options,
        ILogger<IpBlockListMiddleware> logger)
    {
        _next = next;
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is not null && IsRejected(remoteIp))
        {
            _logger.LogWarning("Rejected request from blocked remote IP address.");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }

    private bool IsRejected(IPAddress remoteIp)
    {
        foreach (var configured in _options.CurrentValue.RejectedIpAddresses)
        {
            if (IPAddress.TryParse(configured, out var blockedIp) && blockedIp.Equals(remoteIp))
            {
                return true;
            }
        }

        return false;
    }
}
