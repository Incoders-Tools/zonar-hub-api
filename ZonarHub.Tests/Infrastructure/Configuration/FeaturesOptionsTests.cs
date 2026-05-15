using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZonarHub.Infrastructure.Configuration;

namespace ZonarHub.Tests.Infrastructure.Configuration;

/// <summary>
/// Unit tests for <see cref="FeaturesOptions"/> configuration binding.
/// Task 1.2.1 — RED until FeaturesOptions is implemented.
/// REQ-IMP-007, design §9.1.
/// </summary>
public sealed class FeaturesOptionsTests
{
    [Fact]
    public void ImpersonationEnabled_WhenSetToTrue_ParsesAsTrue()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["Features:Impersonation:Enabled"] = "true",
        });

        Assert.True(options.Impersonation.Enabled);
    }

    [Fact]
    public void ImpersonationEnabled_WhenSetToFalse_ParsesAsFalse()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["Features:Impersonation:Enabled"] = "false",
        });

        Assert.False(options.Impersonation.Enabled);
    }

    [Fact]
    public void ImpersonationEnabled_WhenKeyAbsent_DefaultsToFalse()
    {
        var options = BuildOptions(new Dictionary<string, string?>());

        Assert.False(options.Impersonation.Enabled);
    }

    [Fact]
    public void ImpersonationTokenMinutes_WhenKeyAbsent_DefaultsToThirty()
    {
        var jwtOptions = BuildJwtOptions(new Dictionary<string, string?>
        {
            [ZonarHub.Infrastructure.Auth.JwtOptions.SectionName + ":Secret"] = "test-secret-for-validation-only",
        });

        Assert.Equal(30, jwtOptions.ImpersonationTokenMinutes);
    }

    [Fact]
    public void ImpersonationTokenMinutes_WhenConfigured_ParsesCorrectly()
    {
        var jwtOptions = BuildJwtOptions(new Dictionary<string, string?>
        {
            [ZonarHub.Infrastructure.Auth.JwtOptions.SectionName + ":Secret"] = "test-secret-for-validation-only",
            [ZonarHub.Infrastructure.Auth.JwtOptions.SectionName + ":ImpersonationTokenMinutes"] = "45",
        });

        Assert.Equal(45, jwtOptions.ImpersonationTokenMinutes);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static FeaturesOptions BuildOptions(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<FeaturesOptions>()
            .Bind(config.GetSection(FeaturesOptions.SectionName));

        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptions<FeaturesOptions>>().Value;
    }

    private static ZonarHub.Infrastructure.Auth.JwtOptions BuildJwtOptions(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<ZonarHub.Infrastructure.Auth.JwtOptions>()
            .Bind(config.GetSection(ZonarHub.Infrastructure.Auth.JwtOptions.SectionName));

        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptions<ZonarHub.Infrastructure.Auth.JwtOptions>>().Value;
    }
}
