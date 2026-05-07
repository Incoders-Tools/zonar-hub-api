using ZonarHub.Application.Features.TournamentStatuses.Create;
using ZonarHub.Application.Features.TournamentStatuses.Delete;
using ZonarHub.Application.Features.TournamentStatuses.GetAll;
using ZonarHub.Application.Features.TournamentStatuses.GetById;
using ZonarHub.Application.Features.TournamentStatuses.Update;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.TournamentStatuses;

internal sealed class TournamentStatusesTestHarness
{
    public TournamentStatusesTestHarness(DateTime nowUtc)
    {
        Repository = new InMemoryTournamentStatusRepository();
        Clock = new TestClock(nowUtc);

        GetAll = new GetTournamentStatusesHandler(Repository);
        GetById = new GetTournamentStatusByIdHandler(Repository);
        Create = new CreateTournamentStatusHandler(Repository, Clock);
        Update = new UpdateTournamentStatusHandler(Repository, Clock);
        Delete = new DeleteTournamentStatusHandler(Repository);
    }

    public InMemoryTournamentStatusRepository Repository { get; }
    public TestClock Clock { get; }
    public GetTournamentStatusesHandler GetAll { get; }
    public GetTournamentStatusByIdHandler GetById { get; }
    public CreateTournamentStatusHandler Create { get; }
    public UpdateTournamentStatusHandler Update { get; }
    public DeleteTournamentStatusHandler Delete { get; }
}
