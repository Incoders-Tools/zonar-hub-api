using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.PublicTournaments.GetAll;

/// <param name="Limit">Maximum items; null means <see cref="MaxLimit"/>, other values are clamped to 1..<see cref="MaxLimit"/>.</param>
public sealed record GetPublicTournamentsQuery(int? Limit = null) : IRequest<Result<IReadOnlyList<PublicTournamentResponse>>>
{
    public const int MaxLimit = 100;
}
