namespace ZonarHub.ApiService.Configuration;

/// <summary>
/// Builds the default CORS policy from configuration <c>Cors:AllowedOrigins</c>
/// (environment variables <c>Cors__AllowedOrigins__0</c>, <c>Cors__AllowedOrigins__1</c>, ...).
/// </summary>
/// <remarks>
/// Values are trimmed and normalized to <c>scheme://host[:port]</c>; blanks are skipped and anything that is not an
/// absolute http/https origin is rejected. With no valid origin the policy allows no cross-origin access.
/// </remarks>
internal static class CorsConfiguration
{
    public const string AllowedOriginsKey = "Cors:AllowedOrigins";

    public static CorsOrigins ResolveAllowedOrigins(IConfiguration configuration)
    {
        var allowed = new List<string>();
        var rejected = new List<string>();
        foreach (var entry in configuration.GetSection(AllowedOriginsKey).GetChildren())
        {
            var value = entry.Value?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (TryNormalizeOrigin(value, out var origin))
            {
                if (!allowed.Contains(origin, StringComparer.OrdinalIgnoreCase))
                {
                    allowed.Add(origin);
                }
            }
            else
            {
                rejected.Add(value);
            }
        }

        return new CorsOrigins(allowed, rejected);
    }

    public static IServiceCollection AddConfiguredCors(this IServiceCollection services, CorsOrigins origins)
    {
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                if (origins.Allowed.Count == 0)
                {
                    return;
                }

                policy.WithOrigins([.. origins.Allowed])
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        return services;
    }

    public static void LogStartupWarnings(ILogger logger, CorsOrigins origins)
    {
        foreach (var origin in origins.Rejected)
        {
            logger.LogWarning(
                "Ignoring invalid CORS origin '{Origin}' in {Key}; expected an absolute http or https origin.",
                origin,
                AllowedOriginsKey);
        }

        if (origins.Allowed.Count == 0)
        {
            logger.LogWarning(
                "No CORS origins are configured in {Key}; cross-origin requests are not allowed.",
                AllowedOriginsKey);
        }
    }

    private static bool TryNormalizeOrigin(string value, out string origin)
    {
        origin = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.UserInfo.Length > 0
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            return false;
        }

        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}

/// <summary>Origins accepted for the default CORS policy and the configured values that were rejected.</summary>
internal sealed record CorsOrigins(IReadOnlyList<string> Allowed, IReadOnlyList<string> Rejected);
