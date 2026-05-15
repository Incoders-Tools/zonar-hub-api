using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.Impersonation.Stop;

/// <summary>
/// Command to stop (revoke) an active impersonation session.
/// The caller must be presenting an impersonation JWT that carries <c>imp_session_id</c>.
/// Satisfies: REQ-IMP-020, design §4.1.
/// </summary>
public sealed record StopImpersonationCommand(
    Guid SessionId)
    : IRequest<Result>;
