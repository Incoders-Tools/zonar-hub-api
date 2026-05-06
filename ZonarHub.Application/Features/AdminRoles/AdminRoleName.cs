using System.Text.RegularExpressions;

namespace ZonarHub.Application.Features.AdminRoles;

internal static partial class AdminRoleName
{
    [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.Compiled)]
    private static partial Regex KeyPattern();

    public static string Normalize(string value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValid(string value) => KeyPattern().IsMatch(value);
}
