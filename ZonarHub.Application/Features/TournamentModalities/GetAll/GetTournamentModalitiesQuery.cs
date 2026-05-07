using MediatR;
using ZonarHub.Application.Features.TournamentModalities;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentModalities.GetAll;

public sealed record GetTournamentModalitiesQuery(bool IncludeInactive = false)
    : IRequest<Result<IReadOnlyList<TournamentModalityResponse>>>;
