using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.Create;

public sealed record CreateTournamentStatusCommand(
    string Name,
    string Key,
    string? Description,
    int SortOrder)
    : IRequest<Result<TournamentStatusResponse>>;
