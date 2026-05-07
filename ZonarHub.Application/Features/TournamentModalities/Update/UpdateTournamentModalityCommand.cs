using MediatR;
using ZonarHub.Application.Features.TournamentModalities;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentModalities.Update;

public sealed record UpdateTournamentModalityCommand(
    Guid Id,
    string NameEs,
    string NameEn,
    string NamePt,
    int SortOrder,
    bool IsActive) : IRequest<Result<TournamentModalityResponse>>;
