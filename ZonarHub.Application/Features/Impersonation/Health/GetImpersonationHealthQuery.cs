using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.Impersonation.Health;

/// <summary>
/// Query for the impersonation feature health/availability status.
/// Returns <see cref="ImpersonationHealthResponse"/> based on the feature flag.
/// Anonymous-tolerant — no auth required. Satisfies: design §4.1, §9.1.
/// </summary>
public sealed record GetImpersonationHealthQuery : IRequest<Result<ImpersonationHealthResponse>>;

/// <summary>Response DTO for the health endpoint.</summary>
public sealed record ImpersonationHealthResponse(bool Enabled);
