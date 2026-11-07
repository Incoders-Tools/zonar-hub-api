using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.PublicTournaments.GetBySlug;

public sealed record GetPublicTournamentBySlugQuery(string Slug) : IRequest<Result<PublicTournamentResponse>>;
