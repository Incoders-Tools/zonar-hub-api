using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.Impersonation.Start;

/// <summary>
/// Command to start an impersonation session.
/// Caller must be a system_admin. Feature flag must be enabled.
/// Satisfies: REQ-IMP-001, REQ-IMP-008, REQ-IMP-009, design §4.1.
/// </summary>
public sealed record StartImpersonationCommand(
    Guid TargetUserId,
    string? Reason)
    : IRequest<Result<StartImpersonationResponse>>;
