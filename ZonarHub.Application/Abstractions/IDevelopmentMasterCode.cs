namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Development-only verification bypass for sign-up codes. Infrastructure enables it only when the host
/// environment is Development and a non-empty code is configured; everywhere else no code matches.
/// </summary>
public interface IDevelopmentMasterCode
{
    /// <summary><c>true</c> when the bypass is enabled and <paramref name="code"/> equals the configured code.</summary>
    bool Matches(string? code);
}
