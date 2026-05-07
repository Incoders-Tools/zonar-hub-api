using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.Update;

public sealed record UpdateTournamentStatusCommand(
    Guid Id,
    string NameEs,
    string NameEn,
    string NamePt,
    string? DescriptionEs,
    string? DescriptionEn,
    string? DescriptionPt,
    int SortOrder,
    bool IsActive)
    : IRequest<Result<TournamentStatusResponse>>;
