using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Courts.Create;

public sealed record CreateCourtCommand(
    Guid ComplexId,
    string Name
) : IRequest<Result<Guid>>;
