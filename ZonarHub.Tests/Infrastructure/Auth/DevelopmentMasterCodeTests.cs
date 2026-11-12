using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ZonarHub.Application;
using ZonarHub.Application.Features.Auth.CheckCode;
using ZonarHub.Infrastructure.Auth;
using ZonarHub.Infrastructure.DependencyInjection;

namespace ZonarHub.Tests.Infrastructure.Auth;

public sealed class DevelopmentMasterCodeTests
{
    [Fact]
    public void Matches_InDevelopmentWithConfiguredCode_AcceptsOnlyThatCode()
    {
        var masterCode = Build(Environments.Development, "246810");

        Assert.True(masterCode.Matches("246810"));
        Assert.True(masterCode.Matches(" 246810 "));
        Assert.False(masterCode.Matches("246811"));
        Assert.False(masterCode.Matches(null));
        Assert.False(masterCode.Matches(string.Empty));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Matches_OutsideDevelopment_RejectsTheConfiguredCode(string environmentName)
    {
        var masterCode = Build(environmentName, "246810");

        Assert.False(masterCode.Matches("246810"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Matches_InDevelopmentWithoutConfiguredCode_RejectsEveryCode(string? configured)
    {
        var masterCode = Build(Environments.Development, configured);

        Assert.False(masterCode.Matches(configured));
        Assert.False(masterCode.Matches(string.Empty));
        Assert.False(masterCode.Matches("451499"));
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public async Task CheckCode_ThroughTheRegisteredServices_HonoursTheCodeOnlyInDevelopment(
        string environmentName,
        bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DevelopmentMasterCode.ConfigurationKey] = "246810",
                ["Supabase:Url"] = "https://synthetic.supabase.test",
                ["Supabase:Key"] = "sb_secret_synthetic-test-value",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment(environmentName));
        services.AddApplication();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new CheckVerificationCodeQuery("nobody@example.com", "246810"));

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    private static DevelopmentMasterCode Build(string environmentName, string? configured)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DevelopmentMasterCode.ConfigurationKey] = configured,
            })
            .Build();

        return new DevelopmentMasterCode(new StubHostEnvironment(environmentName), configuration);
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "ZonarHub.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
