using System.Text.RegularExpressions;

namespace ZonarHub.ApiService.Configuration;

/// <summary>
/// Loads a local, gitignored <c>.env</c> file into the process environment for Development runs,
/// so developers can keep runtime secrets such as <c>Supabase__Key</c> out of tracked files.
/// </summary>
/// <remarks>
/// Lines use <c>KEY=VALUE</c> with the standard <c>__</c> configuration section separator
/// (<c>Supabase__Key</c> maps to <c>Supabase:Key</c>). Comments must be on their own line (<c>#</c>);
/// an optional <c>export </c> prefix and matching surrounding quotes are tolerated; malformed lines are ignored.
/// Variables already present in the process environment are never overridden, and empty values are not applied
/// so they cannot blank out tracked JSON settings. Values are never logged.
/// </remarks>
internal static partial class DotEnvFile
{
    public const string FileName = ".env";
    public const string RepositoryMarker = "ZonarHub.slnx";

    private const string DevelopmentEnvironment = "Development";
    private const string ExportPrefix = "export ";

    /// <summary>Parses <c>.env</c> lines into key/value pairs; the last occurrence of a key wins.</summary>
    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith(ExportPrefix, StringComparison.Ordinal))
            {
                line = line[ExportPrefix.Length..].TrimStart();
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            if (!KeyFormat().IsMatch(key))
            {
                continue;
            }

            entries[key] = Unquote(line[(separator + 1)..].Trim());
        }

        return entries;
    }

    /// <summary>
    /// Returns the <c>.env</c> path in the first repository root (the folder containing <see cref="RepositoryMarker"/>)
    /// found by walking up from each start directory in order, or <see langword="null"/> when none is found.
    /// </summary>
    public static string? FindRepositoryFile(params string[] startDirectories)
    {
        foreach (var start in startDirectories)
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, RepositoryMarker)))
                {
                    return Path.Combine(directory.FullName, FileName);
                }
            }
        }

        return null;
    }

    /// <summary>Loads the file into the process environment.</summary>
    public static DotEnvLoadResult Load(string path) =>
        Load(path, Environment.GetEnvironmentVariable, Environment.SetEnvironmentVariable);

    /// <summary>Loads the file through the supplied environment accessors; a missing file is a no-op.</summary>
    public static DotEnvLoadResult Load(string path, Func<string, string?> getVariable, Action<string, string> setVariable)
    {
        if (!File.Exists(path))
        {
            return DotEnvLoadResult.NotLoaded;
        }

        var loaded = 0;
        foreach (var (key, value) in Parse(File.ReadAllLines(path)))
        {
            if (value.Length == 0 || getVariable(key) is not null)
            {
                continue;
            }

            setVariable(key, value);
            loaded++;
        }

        return new DotEnvLoadResult(path, loaded);
    }

    /// <summary>
    /// Loads the repository <c>.env</c> only when <c>ASPNETCORE_ENVIRONMENT</c> (or, when unset, <c>DOTNET_ENVIRONMENT</c>)
    /// is Development; any other or missing environment keeps Production semantics and loads nothing.
    /// </summary>
    public static DotEnvLoadResult LoadForDevelopment(IEnumerable<string> startDirectories) =>
        LoadForDevelopment(startDirectories, Environment.GetEnvironmentVariable, Environment.SetEnvironmentVariable);

    /// <inheritdoc cref="LoadForDevelopment(IEnumerable{string})"/>
    public static DotEnvLoadResult LoadForDevelopment(
        IEnumerable<string> startDirectories,
        Func<string, string?> getVariable,
        Action<string, string> setVariable)
    {
        var environmentName = getVariable("ASPNETCORE_ENVIRONMENT") ?? getVariable("DOTNET_ENVIRONMENT");
        if (!string.Equals(environmentName, DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            return DotEnvLoadResult.NotLoaded;
        }

        var path = FindRepositoryFile([.. startDirectories]);
        return path is null ? DotEnvLoadResult.NotLoaded : Load(path, getVariable, setVariable);
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
        {
            return value[1..^1];
        }

        return value;
    }

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyFormat();
}

/// <summary>Outcome of a <c>.env</c> load: the file used (if any) and how many variables were set.</summary>
internal sealed record DotEnvLoadResult(string? Path, int LoadedCount)
{
    public static DotEnvLoadResult NotLoaded { get; } = new(null, 0);
}
