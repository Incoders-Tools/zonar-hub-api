using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminTournaments.Delete;

public sealed record DeleteTournamentCommand(Guid Id) : IRequest<Result>;
