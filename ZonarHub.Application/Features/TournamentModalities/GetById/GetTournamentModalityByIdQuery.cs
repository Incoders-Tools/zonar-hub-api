using MediatR;
using ZonarHub.Application.Features.TournamentModalities;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentModalities.GetById;

public sealed record GetTournamentModalityByIdQuery(Guid Id) : IRequest<Result<TournamentModalityResponse>>;
