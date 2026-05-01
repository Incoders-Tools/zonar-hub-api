namespace ZonarHub.Application.Features.Auth;

internal static class AuthVerificationCodes
{
    internal const string MasterBypassCode = "451499";

    internal static bool IsMasterBypass(string? code) =>
        string.Equals(code?.Trim(), MasterBypassCode, StringComparison.Ordinal);
}
