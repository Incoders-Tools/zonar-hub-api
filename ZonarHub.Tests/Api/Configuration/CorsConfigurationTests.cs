using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZonarHub.ApiService.Configuration;

namespace ZonarHub.Tests.Api.Configuration;

public sealed class CorsConfigurationTests
{
    private static readonly string[] DevelopmentOrigins =
    [
        "http://localhost:4200", "https://localhost:4200",
        "http://127.0.0.1:4200", "https://127.0.0.1:4200",
    ];

    [Fact]
    public void ResolveAllowedOrigins_TrimsNormalizesAndDeduplicatesConfiguredOrigins()
    {
        var origins = CorsConfiguration.ResolveAllowedOrigins(Configuration(
            "  https://app.example.com  ",
            "https://APP.example.com/",
            "http://localhost:4200"));

        Assert.Equal(["https://app.example.com", "http://localhost:4200"], origins.Allowed);
        Assert.Empty(origins.Rejected);
    }

    [Fact]
    public void ResolveAllowedOrigins_SkipsBlanksAndRejectsInvalidOrigins()
    {
        var origins = CorsConfiguration.ResolveAllowedOrigins(Configuration(
            "",
            "   ",
            "*",
            "app.example.com",
            "ftp://files.example.com",
            "https://app.example.com/path",
            "https://app.example.com?query=1",
            "https://user@app.example.com",
            "https://ok.example.com"));

        Assert.Equal(["https://ok.example.com"], origins.Allowed);
        Assert.Equal(
            ["*", "app.example.com", "ftp://files.example.com", "https://app.example.com/path", "https://app.example.com?query=1", "https://user@app.example.com"],
            origins.Rejected);
    }

    [Fact]
    public void ResolveAllowedOrigins_WithoutTheSection_ReturnsNoOrigins()
    {
        var origins = CorsConfiguration.ResolveAllowedOrigins(new ConfigurationBuilder().Build());

        Assert.Empty(origins.Allowed);
        Assert.Empty(origins.Rejected);
    }

    [Fact]
    public void ResolveAllowedOrigins_ReadsTheEnvironmentVariableIndexForm()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Equivalent of the environment variables Cors__AllowedOrigins__0 and Cors__AllowedOrigins__1.
                ["Cors:AllowedOrigins:0"] = "https://zonarhub.example.com",
                ["Cors:AllowedOrigins:1"] = "https://www.zonarhub.example.com",
            })
            .Build();

        var origins = CorsConfiguration.ResolveAllowedOrigins(configuration);

        Assert.Equal(["https://zonarhub.example.com", "https://www.zonarhub.example.com"], origins.Allowed);
    }

    [Fact]
    public void AddConfiguredCors_AppliesConfiguredOriginsWithCredentialsAnyHeaderAndAnyMethod()
    {
        var policy = DefaultPolicy(new CorsOrigins(["https://app.example.com"], []));

        Assert.Equal(["https://app.example.com"], policy.Origins);
        Assert.True(policy.IsOriginAllowed("https://app.example.com"));
        Assert.False(policy.IsOriginAllowed("http://localhost:4200"));
        Assert.True(policy.SupportsCredentials);
        Assert.True(policy.AllowAnyHeader);
        Assert.True(policy.AllowAnyMethod);
    }

    [Fact]
    public void AddConfiguredCors_WithNoOrigins_AllowsNoCrossOriginAccess()
    {
        var policy = DefaultPolicy(new CorsOrigins([], []));

        Assert.Empty(policy.Origins);
        Assert.False(policy.AllowAnyOrigin);
        Assert.False(policy.IsOriginAllowed("http://localhost:4200"));
        Assert.False(policy.SupportsCredentials);
    }

    [Fact]
    public void LogStartupWarnings_WarnsOncePerRejectedOriginAndOnceWhenTheListIsEmpty()
    {
        var logger = new RecordingLogger();

        CorsConfiguration.LogStartupWarnings(logger, new CorsOrigins([], ["*", "ftp://files.example.com"]));

        Assert.Equal(
            [
                "Ignoring invalid CORS origin '*' in Cors:AllowedOrigins; expected an absolute http or https origin.",
                "Ignoring invalid CORS origin 'ftp://files.example.com' in Cors:AllowedOrigins; expected an absolute http or https origin.",
                "No CORS origins are configured in Cors:AllowedOrigins; cross-origin requests are not allowed.",
            ],
            logger.Entries.Select(entry => entry.Message));
        Assert.All(logger.Entries, entry => Assert.Equal(LogLevel.Warning, entry.Level));
    }

    [Fact]
    public void LogStartupWarnings_WithValidOrigins_LogsNoWarning()
    {
        var logger = new RecordingLogger();

        CorsConfiguration.LogStartupWarnings(logger, new CorsOrigins(["https://app.example.com"], []));

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void TrackedSettings_ConfigureLocalhostOriginsOnlyForDevelopment()
    {
        var projectDirectory = Path.Combine(RepositoryRoot(), "ZonarHub.ApiService");

        var production = CorsConfiguration.ResolveAllowedOrigins(new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(projectDirectory, "appsettings.json"))
            .Build());
        var development = CorsConfiguration.ResolveAllowedOrigins(new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(projectDirectory, "appsettings.json"))
            .AddJsonFile(Path.Combine(projectDirectory, "appsettings.Development.json"))
            .Build());

        Assert.Empty(production.Allowed);
        Assert.Equal(DevelopmentOrigins, development.Allowed);
        Assert.Empty(development.Rejected);
    }

    private static IConfiguration Configuration(params string[] origins) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(origins.Select((origin, index) =>
                new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{index}", origin)))
            .Build();

    private static CorsPolicy DefaultPolicy(CorsOrigins origins)
    {
        var services = new ServiceCollection();
        services.AddConfiguredCors(origins);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        return Assert.IsType<CorsPolicy>(options.GetPolicy(options.DefaultPolicyName));
    }

    private static string RepositoryRoot()
    {
        var envPath = DotEnvFile.FindRepositoryFile(AppContext.BaseDirectory);
        Assert.NotNull(envPath);
        return Path.GetDirectoryName(envPath)!;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
