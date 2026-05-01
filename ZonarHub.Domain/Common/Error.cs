namespace ZonarHub.Domain.Common;

/// <summary>
/// Represents a stable, machine-readable domain error.
/// </summary>
/// <remarks>
/// The <see cref="Code"/> is a stable identifier safe for clients and logs.
/// The <see cref="MessageKey"/> is a localization key resolved at the API boundary.
/// </remarks>
public sealed record Error(string Code, string MessageKey, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    public static Error Validation(string code, string messageKey) => new(code, messageKey, ErrorType.Validation);

    public static Error NotFound(string code, string messageKey) => new(code, messageKey, ErrorType.NotFound);

    public static Error Conflict(string code, string messageKey) => new(code, messageKey, ErrorType.Conflict);

    public static Error Failure(string code, string messageKey) => new(code, messageKey, ErrorType.Failure);
}

public enum ErrorType
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Failure = 4,
}
