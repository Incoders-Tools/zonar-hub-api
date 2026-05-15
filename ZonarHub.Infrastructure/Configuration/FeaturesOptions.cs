namespace ZonarHub.Infrastructure.Configuration;

/// <summary>
/// Top-level feature-flag options bound from the <c>Features</c> configuration section.
/// Satisfies: REQ-IMP-007, design §9.1.
/// </summary>
public sealed class FeaturesOptions
{
    public const string SectionName = "Features";

    /// <summary>Impersonation feature-flag sub-section.</summary>
    public ImpersonationFeatureOptions Impersonation { get; init; } = new();

    /// <summary>Per-feature flag options for the impersonation capability.</summary>
    public sealed class ImpersonationFeatureOptions
    {
        /// <summary>
        /// When <c>false</c> (default) the impersonation entry point is hidden and
        /// <c>POST /api/admin/impersonation/start</c> returns 404.
        /// Flip to <c>true</c> to enable the feature without a redeploy.
        /// </summary>
        public bool Enabled { get; init; } = false;
    }
}
