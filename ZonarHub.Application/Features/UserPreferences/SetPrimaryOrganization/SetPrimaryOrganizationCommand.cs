using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.UserPreferences.SetPrimaryOrganization;

/// <summary>Selects an assigned active organization as the caller's persisted primary.</summary>
public sealed record SetPrimaryOrganizationCommand(Guid OrganizationId) : IRequest<Result>;
