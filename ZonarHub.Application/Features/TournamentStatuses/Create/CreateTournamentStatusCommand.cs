using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.Create;

public sealed record CreateTournamentStatusCommand(
    string Key,
    string NameEs,
    string NameEn,
    string NamePt,
    string? DescriptionEs,
    string? DescriptionEn,
    string? DescriptionPt,
    int SortOrder)
    : IRequest<Result<TournamentStatusResponse>>;
