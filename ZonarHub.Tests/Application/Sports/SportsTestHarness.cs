using ZonarHub.Application.Features.Sports.Create;
using ZonarHub.Application.Features.Sports.Delete;
using ZonarHub.Application.Features.Sports.GetAll;
using ZonarHub.Application.Features.Sports.GetById;
using ZonarHub.Application.Features.Sports.Update;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.Sports;

internal sealed class SportsTestHarness
{
    public SportsTestHarness(DateTime nowUtc)
    {
        Store = new InMemorySportStore();
        var repo = new InMemorySportRepository(Store);
        var uow = new InMemoryUnitOfWork();
        Clock = new TestClock(nowUtc);
        Create = new CreateSportHandler(repo, uow, Clock);
        GetById = new GetSportByIdHandler(repo);
        List = new GetSportsHandler(repo);
        Update = new UpdateSportHandler(repo, uow, Clock);
        Delete = new DeleteSportHandler(repo, uow);
    }

    public InMemorySportStore Store { get; }
    public TestClock Clock { get; }
    public CreateSportHandler Create { get; }
    public GetSportByIdHandler GetById { get; }
    public GetSportsHandler List { get; }
    public UpdateSportHandler Update { get; }
    public DeleteSportHandler Delete { get; }
}
