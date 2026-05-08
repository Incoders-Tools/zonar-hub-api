using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentRules.Update;

public sealed record UpdateTournamentRuleCommand(
    Guid Id,
    string Name,
    string? DescriptionEs,
    string? DescriptionEn,
    string? DescriptionPt,
    int SortOrder,
    bool IsActive)
    : IRequest<Result<TournamentRuleResponse>>;
