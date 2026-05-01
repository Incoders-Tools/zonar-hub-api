using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.SocialNetworks.Delete;

public sealed record DeleteSocialNetworkCommand(Guid Id) : IRequest<Result>;
