using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.Update;

public sealed record UpdateTournamentStatusCommand(
    string Id,
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive)
    : IRequest<Result<TournamentStatusResponse>>;
