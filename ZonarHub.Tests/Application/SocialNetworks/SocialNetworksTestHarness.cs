using ZonarHub.Application.Features.SocialNetworks.Create;
using ZonarHub.Application.Features.SocialNetworks.Delete;
using ZonarHub.Application.Features.SocialNetworks.GetAll;
using ZonarHub.Application.Features.SocialNetworks.GetById;
using ZonarHub.Application.Features.SocialNetworks.Update;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.SocialNetworks;

internal sealed class SocialNetworksTestHarness
{
    public SocialNetworksTestHarness(DateTime nowUtc)
    {
        Store = new InMemorySocialNetworkStore();
        var repo = new InMemorySocialNetworkRepository(Store);
        var uow = new InMemoryUnitOfWork();
        Clock = new TestClock(nowUtc);
        Create = new CreateSocialNetworkHandler(repo, uow, Clock);
        GetById = new GetSocialNetworkByIdHandler(repo);
        List = new GetSocialNetworksHandler(repo);
        Update = new UpdateSocialNetworkHandler(repo, uow, Clock);
        Delete = new DeleteSocialNetworkHandler(repo, uow);
    }

    public InMemorySocialNetworkStore Store { get; }
    public TestClock Clock { get; }
    public CreateSocialNetworkHandler Create { get; }
    public GetSocialNetworkByIdHandler GetById { get; }
    public GetSocialNetworksHandler List { get; }
    public UpdateSocialNetworkHandler Update { get; }
    public DeleteSocialNetworkHandler Delete { get; }
}
