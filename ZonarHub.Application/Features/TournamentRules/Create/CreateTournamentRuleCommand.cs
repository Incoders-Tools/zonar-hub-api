using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentRules.Create;

public sealed record CreateTournamentRuleCommand(
    string Name,
    string? DescriptionEs,
    string? DescriptionEn,
    string? DescriptionPt,
    int SortOrder)
    : IRequest<Result<TournamentRuleResponse>>;
