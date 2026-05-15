namespace ZonarHub.Infrastructure.Middleware;

/// <summary>
/// Provides HTTP context information for impersonation audit rows.
/// Decouples the MediatR pipeline behavior from <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>.
/// </summary>
public interface IHttpContextInfo
{
    string Method { get; }
    string Path { get; }
    string Ip { get; }
    string UserAgent { get; }
}
