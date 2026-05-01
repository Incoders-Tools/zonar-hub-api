using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Time;

/// <summary>
/// Default UTC clock backed by <see cref="TimeProvider"/>.
/// </summary>
public sealed class SystemClock : IClock
{
    private readonly TimeProvider _timeProvider;

    public SystemClock(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;
}
