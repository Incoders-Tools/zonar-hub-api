using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZonarHub.Infrastructure.DependencyInjection;
using ZonarHub.Infrastructure.Persistence.Supabase;

namespace ZonarHub.Tests.Infrastructure.Supabase;

/// <summary>
/// Verifies the named Supabase HttpClient headers for each supported API key shape.
/// All keys are synthetic placeholders; no real credential is used.
/// </summary>
public class SupabaseHttpClientConfigurationTests
{
    private const string SyntheticSecretKey = "sb_secret_synthetic-test-value";
    private const string SyntheticPublishableKey = "sb_publishable_synthetic-test-value";
    private const string SyntheticLegacyJwt = "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoic3ludGhldGljIn0.c3ludGhldGlj";
    private const string Canary = "LeakCanary7";
    private const string UnsupportedFormatMessage =
        "Supabase:Key has an unsupported format; expected a server-side sb_secret_ key or a legacy JWT.";

    [Fact]
    public void SecretKey_IsSentAsApiKeyOnly_WithoutBearerAuthorization()
    {
        using var provider = BuildProvider(SyntheticSecretKey);

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(SupabaseHttpClientName.Name);

        Assert.Equal(SyntheticSecretKey, Assert.Single(client.DefaultRequestHeaders.GetValues("apikey")));
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.Equal(new Uri("https://synthetic.supabase.test/"), client.BaseAddress);
    }

    [Fact]
    public void LegacyJwtKey_IsSentAsApiKeyAndBearerAuthorization()
    {
        using var provider = BuildProvider(SyntheticLegacyJwt);

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(SupabaseHttpClientName.Name);

        Assert.Equal(SyntheticLegacyJwt, Assert.Single(client.DefaultRequestHeaders.GetValues("apikey")));
        Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal(SyntheticLegacyJwt, client.DefaultRequestHeaders.Authorization?.Parameter);
    }

    [Fact]
    public void PublishableKey_IsRejected_WithNonSecretError()
    {
        using var provider = BuildProvider(SyntheticPublishableKey);

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<SupabaseOptions>>().Value);

        Assert.Contains("publishable", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SyntheticPublishableKey, ex.Message);
        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IHttpClientFactory>().CreateClient(SupabaseHttpClientName.Name));
    }

    /// <summary>
    /// Each value embeds <see cref="Canary"/> so the assertions can prove the rejected value is never echoed.
    /// </summary>
    [Theory]
    [InlineData(" sb_secret_LeakCanary7")]
    [InlineData("sb_secret_LeakCanary7 ")]
    [InlineData("sb_secret_LeakCanary7\n")]
    [InlineData("sb_secret_LeakCanary7\r\nX-Injected: 1")]
    [InlineData("sb_secret_Leak\tCanary7")]
    [InlineData("sb_secret_LeakCanary7\u0000")]
    [InlineData("sb_secret_LeakCanary7ä")]
    [InlineData("sb_secret_LeakCanary7.a.b")]
    [InlineData("SB_SECRET_LeakCanary7")]
    [InlineData("Sb_Secret_LeakCanary7")]
    [InlineData("sb_publishable_LeakCanary7")]
    [InlineData("SB_PUBLISHABLE_LeakCanary7")]
    [InlineData("unknown-LeakCanary7")]
    [InlineData("eyJLeakCanary7.eyJyb2xlIjoic3ludGhldGljIn0")]
    [InlineData("eyJLeakCanary7.eyJyb2xlIjoic3ludGhldGljIn0.c2ln.extra")]
    [InlineData("eyJLeakCanary7..c2ln")]
    [InlineData("eyJLeakCanary7.eyJyb2xlIjoic3ludGhldGljIn0.c2ln=")]
    [InlineData(" eyJLeakCanary7.eyJyb2xlIjoic3ludGhldGljIn0.c2ln")]
    [InlineData("eyJLeakCanary7.eyJyb2xlIjoic3ludGhldGljIn0.c2ln\n")]
    [InlineData("LeakCanary7.eyJyb2xlIjoic3ludGhldGljIn0.c2ln")]
    [InlineData("SB_SECRET_LeakCanary7.eyJyb2xlIjoic3ludGhldGljIn0.c2ln")]
    public void UnsupportedKeyFormat_IsRejectedBeforeHeaderConstruction_WithoutEchoingValue(string key)
    {
        using var provider = BuildProvider(key);

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<SupabaseOptions>>().Value);

        Assert.Contains(UnsupportedFormatMessage, ex.Failures);
        Assert.DoesNotContain(Canary, ex.Message);
        Assert.DoesNotContain(ex.Failures, f => f.Contains(Canary, StringComparison.Ordinal));

        // Client creation must fail on options validation, never on a header FormatException.
        var clientEx = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IHttpClientFactory>().CreateClient(SupabaseHttpClientName.Name));
        Assert.DoesNotContain(Canary, clientEx.Message);
    }

    [Fact]
    public void MissingKey_IsRejected_AsRequired()
    {
        using var provider = BuildProvider(null);

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<SupabaseOptions>>().Value);

        Assert.Contains("Supabase:Key is required.", ex.Failures);
        Assert.DoesNotContain(ex.Failures, f => f.Contains("publishable", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(UnsupportedFormatMessage, ex.Failures);
    }

    private static ServiceProvider BuildProvider(string? key)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Supabase:Url"] = "https://synthetic.supabase.test",
                ["Supabase:Key"] = key,
            })
            .Build();

        return new ServiceCollection()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
    }
}
