using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Courts.Delete;

public sealed record DeleteCourtCommand(Guid Id) : IRequest<Result>;
