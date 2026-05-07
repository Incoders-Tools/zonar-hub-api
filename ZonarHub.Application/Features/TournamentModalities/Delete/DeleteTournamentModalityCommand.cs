using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentModalities.Delete;

public sealed record DeleteTournamentModalityCommand(Guid Id) : IRequest<Result>;
