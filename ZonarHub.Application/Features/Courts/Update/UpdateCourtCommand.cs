using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Courts.Update;

public sealed record UpdateCourtCommand(
    Guid Id,
    string Name,
    bool IsActive
) : IRequest<Result>;
