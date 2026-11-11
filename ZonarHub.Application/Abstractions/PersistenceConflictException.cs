using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Raised by a repository write that the store refused for a business reason (a unique rule, a referenced row, a state
/// or concurrency check); nothing was written. <see cref="Error"/> is the domain error with the stable code the use case
/// returns to the client, e.g. <c>registrations.capacity_reached</c> or <c>players.email_taken</c>.
/// </summary>
public sealed class PersistenceConflictException(Error error)
    : Exception($"The store refused the write: {error.Code}.")
{
    public Error Error { get; } = error;
}
