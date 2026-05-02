using ZonarHub.Application.Features.Courts.GetAll;
using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Courts.GetById;

public sealed record GetCourtByIdQuery(Guid Id) : IRequest<Result<CourtDto>>;
