using MediatR;
using ZonarHub.Application.Features.TournamentModalities;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentModalities.Create;

public sealed record CreateTournamentModalityCommand(
    string NameEs,
    string NameEn,
    string NamePt,
    string Key,
    int SortOrder) : IRequest<Result<TournamentModalityResponse>>;
