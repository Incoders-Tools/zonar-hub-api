using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentRules.Delete;

public sealed record DeleteTournamentRuleCommand(Guid Id) : IRequest<Result>;
