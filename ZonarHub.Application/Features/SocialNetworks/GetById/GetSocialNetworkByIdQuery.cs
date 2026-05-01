using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.SocialNetworks.GetById;

public sealed record GetSocialNetworkByIdQuery(Guid Id) : IRequest<Result<SocialNetworkResponse>>;
