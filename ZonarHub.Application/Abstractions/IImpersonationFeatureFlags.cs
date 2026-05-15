namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Abstracts the feature-flag state for the impersonation capability.
/// Infrastructure wires this to <c>IOptions&lt;FeaturesOptions&gt;</c>.
/// Satisfies: design §9.1, REQ-IMP-007.
/// </summary>
public interface IImpersonationFeatureFlags
{
    /// <summary>
    /// <c>true</c> when the impersonation feature is enabled in configuration.
    /// Evaluated per-request (not cached at startup) so config reloads take effect.
    /// </summary>
    bool IsEnabled { get; }
}
