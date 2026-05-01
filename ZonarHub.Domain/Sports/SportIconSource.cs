namespace ZonarHub.Domain.Sports;

/// <summary>
/// How the sport icon is rendered. Matches the frontend <c>SportIconSource</c> union type.
/// </summary>
public enum SportIconSource
{
    Unicode = 0,
    Svg = 1,
}
