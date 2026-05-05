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
        Store = new InMemorySystemSettingStore();
        var settings = new InMemorySystemSettingRepository(Store);
        var unitOfWork = new InMemoryUnitOfWork();
        Clock = new TestClock(nowUtc);

        GetAll = new GetTournamentStatusesHandler(settings, Clock);
        GetById = new GetTournamentStatusByIdHandler(settings, Clock);
        Create = new CreateTournamentStatusHandler(settings, unitOfWork, Clock);
        Update = new UpdateTournamentStatusHandler(settings, unitOfWork, Clock);
        Delete = new DeleteTournamentStatusHandler(settings, unitOfWork, Clock);
    }

    public InMemorySystemSettingStore Store { get; }
    public TestClock Clock { get; }
    public GetTournamentStatusesHandler GetAll { get; }
    public GetTournamentStatusByIdHandler GetById { get; }
    public CreateTournamentStatusHandler Create { get; }
    public UpdateTournamentStatusHandler Update { get; }
    public DeleteTournamentStatusHandler Delete { get; }
}
