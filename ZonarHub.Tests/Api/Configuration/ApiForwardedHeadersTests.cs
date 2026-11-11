using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZonarHub.ApiService.Security;

namespace ZonarHub.Tests.Api.Configuration;

/// <summary>
/// Behind the hosting proxy every connection comes from the proxy, so rate limits and the IP block list need the client
/// address the proxy appends to <c>X-Forwarded-For</c>. That header is honoured only when
/// <c>Security:TrustForwardedHeaders</c> is true, and only its last hop (the one the proxy added) is used.
/// </summary>
public class ApiForwardedHeadersTests
{
    private const string ProxyIp = "10.0.0.9";

    [Fact]
    public async Task ByDefault_TheForwardedHeaderIsIgnored()
    {
        var seen = await SeenRemoteIp([], forwardedFor: "203.0.113.7");

        Assert.Equal(ProxyIp, seen);
    }

    [Fact]
    public async Task WhenTrusted_TheProxyAppendedClientAddressIsTheRemoteIp()
    {
        var seen = await SeenRemoteIp(new() { ["Security:TrustForwardedHeaders"] = "true" }, forwardedFor: "203.0.113.7");

        Assert.Equal("203.0.113.7", seen);
    }

    [Fact]
    public async Task WhenTrusted_OnlyTheLastHopIsUsed_SoAClientCannotSpoofAnEarlierOne()
    {
        var seen = await SeenRemoteIp(
            new() { ["Security:TrustForwardedHeaders"] = "true" },
            forwardedFor: "198.51.100.1, 203.0.113.7");

        Assert.Equal("203.0.113.7", seen);
    }

    [Fact]
    public async Task WhenTrusted_TheBlockListAppliesToTheForwardedClient()
    {
        var status = await Status(
            new()
            {
                ["Security:TrustForwardedHeaders"] = "true",
                ["Security:RejectedIpAddresses:0"] = "203.0.113.7",
            },
            forwardedFor: "203.0.113.7");

        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public void Options_DefaultToNotTrustingForwardedHeaders()
    {
        Assert.False(Provider([]).GetRequiredService<IOptions<ApiSecurityOptions>>().Value.TrustForwardedHeaders);
    }

    private static async Task<string?> SeenRemoteIp(Dictionary<string, string?> settings, string forwardedFor)
    {
        string? seen = null;
        var context = await Run(settings, forwardedFor, ctx =>
        {
            seen = ctx.Connection.RemoteIpAddress?.ToString();
            return Task.CompletedTask;
        });
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        return seen;
    }

    private static async Task<int> Status(Dictionary<string, string?> settings, string forwardedFor) =>
        (await Run(settings, forwardedFor, _ => Task.CompletedTask)).Response.StatusCode;

    private static async Task<HttpContext> Run(
        Dictionary<string, string?> settings,
        string forwardedFor,
        RequestDelegate terminal)
    {
        var provider = Provider(settings);
        var app = new ApplicationBuilder(provider);
        app.UseApiSecurity();
        app.Run(terminal);
        var pipeline = app.Build();

        var context = new DefaultHttpContext { RequestServices = provider };
        context.Connection.RemoteIpAddress = IPAddress.Parse(ProxyIp);
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        await pipeline(context);
        return context;
    }

    private static ServiceProvider Provider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiSecurity(configuration);
        return services.BuildServiceProvider();
    }
}
