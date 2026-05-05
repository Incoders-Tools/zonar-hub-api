using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.UserPreferences.Get;

/// <summary>
/// Query to retrieve all preferences for the current authenticated user.
/// </summary>
public sealed record GetUserPreferencesQuery(
    Guid UserId,
    Guid? OrganizationId) : IRequest<Result<UserPreferencesResponse>>;
