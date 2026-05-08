using ZonarHub.Application.Features.Complexes.Create;
using ZonarHub.Application.Features.Complexes.Delete;
using ZonarHub.Application.Features.Complexes.GetAll;
using ZonarHub.Application.Features.Complexes.Update;
using ZonarHub.Infrastructure.Persistence.InMemory;

namespace ZonarHub.Tests.Application.Complexes;

internal sealed class ComplexesTestHarness
{
    public ComplexesTestHarness()
    {
        Store = new InMemoryComplexStore();
        var repo = new InMemoryComplexRepository(Store);
        UnitOfWork = new RecordingInMemoryUnitOfWork();

        Create = new CreateComplexHandler(repo, UnitOfWork);
        Update = new UpdateComplexHandler(repo);
        Delete = new DeleteComplexHandler(repo);
        List = new GetComplexesHandler(repo);
    }

    public InMemoryComplexStore Store { get; }
    public RecordingInMemoryUnitOfWork UnitOfWork { get; }
    public CreateComplexHandler Create { get; }
    public UpdateComplexHandler Update { get; }
    public DeleteComplexHandler Delete { get; }
    public GetComplexesHandler List { get; }
}
