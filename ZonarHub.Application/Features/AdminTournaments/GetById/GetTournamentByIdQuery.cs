using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminTournaments.GetById;

public sealed record GetTournamentByIdQuery(Guid Id) : IRequest<Result<TournamentResponse>>;
