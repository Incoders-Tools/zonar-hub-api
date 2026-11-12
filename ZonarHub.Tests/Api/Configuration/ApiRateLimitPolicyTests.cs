using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZonarHub.ApiService.Security;

namespace ZonarHub.Tests.Api.Configuration;

/// <summary>
/// The anonymous rate limit policies: each caller (remote IP) gets a fixed window of its own; the public registration
/// code policy is stricter than the anonymous auth one and configurable under <c>Security</c>.
/// </summary>
public class ApiRateLimitPolicyTests
{
    [Fact]
    public void PublicRegistrationCode_AllowsTheConfiguredPermitsPerCallerAndWindow_ThenRejects()
    {
        var provider = Provider(new() { ["Security:PublicRegistrationCodePermitLimit"] = "2" });
        var first = Context(provider, "10.0.0.1");
        var other = Context(provider, "10.0.0.2");

        var partition = ApiSecurityExtensions.PolicyPartition(ApiRateLimitPolicies.PublicRegistrationCode, first);
        using var limiter = partition.Factory(partition.PartitionKey);

        Assert.True(limiter.AttemptAcquire().IsAcquired);
        Assert.True(limiter.AttemptAcquire().IsAcquired);
        Assert.False(limiter.AttemptAcquire().IsAcquired);
        Assert.NotEqual(partition.PartitionKey, ApiSecurityExtensions.PolicyPartition(ApiRateLimitPolicies.PublicRegistrationCode, other).PartitionKey);
    }

    [Fact]
    public void AnonymousAuth_KeepsItsOwnLimit()
    {
        var provider = Provider(new() { ["Security:AnonymousAuthPermitLimit"] = "1", ["Security:PublicRegistrationCodePermitLimit"] = "5" });

        var partition = ApiSecurityExtensions.PolicyPartition(ApiRateLimitPolicies.AnonymousAuth, Context(provider, "10.0.0.1"));
        using var limiter = partition.Factory(partition.PartitionKey);

        Assert.True(limiter.AttemptAcquire().IsAcquired);
        Assert.False(limiter.AttemptAcquire().IsAcquired);
    }

    [Fact]
    public void Options_DefaultToTenCodeRequestsPerWindow_AndRejectANonPositiveLimit()
    {
        Assert.Equal(10, Provider([]).GetRequiredService<IOptions<ApiSecurityOptions>>().Value.PublicRegistrationCodePermitLimit);

        var invalid = Provider(new() { ["Security:PublicRegistrationCodePermitLimit"] = "0" });
        var error = Assert.Throws<OptionsValidationException>(() => invalid.GetRequiredService<IOptions<ApiSecurityOptions>>().Value);
        Assert.Contains("Security:PublicRegistrationCodePermitLimit must be greater than zero.", error.Failures);
    }

    private static ServiceProvider Provider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiSecurity(configuration);
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext Context(IServiceProvider provider, string ip)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return context;
    }
}
