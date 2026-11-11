using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Auth;

/// <summary>
/// Reads <c>Authentication:DevelopmentMasterCode</c> (environment variable
/// <c>Authentication__DevelopmentMasterCode</c>) once. The bypass is honoured only in the Development
/// environment with a non-empty value; any other environment, or an empty value, disables it.
/// </summary>
internal sealed class DevelopmentMasterCode : IDevelopmentMasterCode
{
    internal const string ConfigurationKey = "Authentication:DevelopmentMasterCode";

    private readonly string? _code;

    public DevelopmentMasterCode(IHostEnvironment environment, IConfiguration configuration)
    {
        var configured = configuration[ConfigurationKey]?.Trim();
        _code = environment.IsDevelopment() && !string.IsNullOrEmpty(configured) ? configured : null;
    }

    public bool Matches(string? code) =>
        _code is not null && string.Equals(code?.Trim(), _code, StringComparison.Ordinal);
}
