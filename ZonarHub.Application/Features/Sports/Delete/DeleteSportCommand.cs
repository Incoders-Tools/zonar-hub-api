using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Sports.Delete;

public sealed record DeleteSportCommand(Guid Id) : IRequest<Result>;
