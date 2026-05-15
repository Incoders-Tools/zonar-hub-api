using Microsoft.Extensions.Options;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Configuration;

/// <summary>
/// Infrastructure implementation of <see cref="IImpersonationFeatureFlags"/>.
/// Reads the current value from <see cref="FeaturesOptions"/> per-request
/// so hot-reload configuration changes take effect without a restart.
/// Satisfies: design §9.1.
/// </summary>
internal sealed class ImpersonationFeatureFlags : IImpersonationFeatureFlags
{
    private readonly IOptionsMonitor<FeaturesOptions> _optionsMonitor;

    public ImpersonationFeatureFlags(IOptionsMonitor<FeaturesOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public bool IsEnabled => _optionsMonitor.CurrentValue.Impersonation.Enabled;
}
