using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Sports.GetById;

public sealed record GetSportByIdQuery(Guid Id) : IRequest<Result<SportResponse>>;
