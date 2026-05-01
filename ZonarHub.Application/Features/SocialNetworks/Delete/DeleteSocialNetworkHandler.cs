using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.SocialNetworks;
using MediatR;

namespace ZonarHub.Application.Features.SocialNetworks.Delete;

public sealed class DeleteSocialNetworkHandler : IRequestHandler<DeleteSocialNetworkCommand, Result>
{
    private readonly ISocialNetworkRepository _networks;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteSocialNetworkHandler(ISocialNetworkRepository networks, IUnitOfWork unitOfWork)
    {
        _networks = networks;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteSocialNetworkCommand request, CancellationToken cancellationToken)
    {
        var network = await _networks.GetByIdAsync(new SocialNetworkId(request.Id), cancellationToken);
        if (network is null)
        {
            return Result.Failure(SocialNetworkErrors.NotFound);
        }

        _networks.Remove(network);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
