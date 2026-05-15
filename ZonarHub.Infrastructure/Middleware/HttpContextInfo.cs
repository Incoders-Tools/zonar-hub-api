using Microsoft.AspNetCore.Http;

namespace ZonarHub.Infrastructure.Middleware;

/// <summary>
/// Production implementation of <see cref="IHttpContextInfo"/> backed by <see cref="IHttpContextAccessor"/>.
/// </summary>
internal sealed class HttpContextInfo : IHttpContextInfo
{
    public HttpContextInfo(IHttpContextAccessor accessor)
    {
        var ctx = accessor.HttpContext;
        Method = ctx?.Request.Method ?? string.Empty;
        Path = ctx?.Request.Path.Value ?? string.Empty;
        Ip = ctx?.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        UserAgent = ctx?.Request.Headers.UserAgent.ToString() ?? string.Empty;
    }

    public string Method { get; }
    public string Path { get; }
    public string Ip { get; }
    public string UserAgent { get; }
}
