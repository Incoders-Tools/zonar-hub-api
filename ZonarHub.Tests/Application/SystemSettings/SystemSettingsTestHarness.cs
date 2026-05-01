using ZonarHub.Application.Features.SystemSettings.Create;
using ZonarHub.Application.Features.SystemSettings.Delete;
using ZonarHub.Application.Features.SystemSettings.GetAll;
using ZonarHub.Application.Features.SystemSettings.GetById;
using ZonarHub.Application.Features.SystemSettings.Update;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.SystemSettings;

internal sealed class SystemSettingsTestHarness
{
    public SystemSettingsTestHarness(DateTime nowUtc)
    {
        Store = new InMemorySystemSettingStore();
        var repo = new InMemorySystemSettingRepository(Store);
        var uow = new InMemoryUnitOfWork();
        Clock = new TestClock(nowUtc);
        Create = new CreateSystemSettingHandler(repo, uow, Clock);
        GetById = new GetSystemSettingByIdHandler(repo);
        List = new GetSystemSettingsHandler(repo);
        Update = new UpdateSystemSettingHandler(repo, uow, Clock);
        Delete = new DeleteSystemSettingHandler(repo, uow);
    }

    public InMemorySystemSettingStore Store { get; }
    public TestClock Clock { get; }
    public CreateSystemSettingHandler Create { get; }
    public GetSystemSettingByIdHandler GetById { get; }
    public GetSystemSettingsHandler List { get; }
    public UpdateSystemSettingHandler Update { get; }
    public DeleteSystemSettingHandler Delete { get; }
}
