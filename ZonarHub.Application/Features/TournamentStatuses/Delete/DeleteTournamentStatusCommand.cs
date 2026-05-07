using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.Delete;

public sealed record DeleteTournamentStatusCommand(Guid Id) : IRequest<Result>;
