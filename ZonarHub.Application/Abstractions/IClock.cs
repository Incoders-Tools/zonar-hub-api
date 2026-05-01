namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Source of UTC time. All domain timestamps cross boundaries through this clock.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
