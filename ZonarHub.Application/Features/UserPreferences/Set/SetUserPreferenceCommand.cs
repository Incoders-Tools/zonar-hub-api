using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.UserPreferences.Set;

/// <summary>
/// Command to set or update a single user preference.
/// If the preference exists, it will be updated; otherwise, it will be created.
/// </summary>
public sealed record SetUserPreferenceCommand(
    Guid UserId,
    Guid? OrganizationId,
    string Key,
    string Value) : IRequest<Result>;
