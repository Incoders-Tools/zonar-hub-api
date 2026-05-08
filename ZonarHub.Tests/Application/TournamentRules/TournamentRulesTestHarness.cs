using ZonarHub.Application.Features.TournamentRules.Create;
using ZonarHub.Application.Features.TournamentRules.Delete;
using ZonarHub.Application.Features.TournamentRules.GetAll;
using ZonarHub.Application.Features.TournamentRules.GetById;
using ZonarHub.Application.Features.TournamentRules.Update;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.TournamentRules;

internal sealed class TournamentRulesTestHarness
{
    public TournamentRulesTestHarness(DateTime nowUtc)
    {
        Repository = new InMemoryTournamentRuleRepository();
        Clock = new TestClock(nowUtc);

        GetAll = new GetTournamentRulesHandler(Repository);
        GetById = new GetTournamentRuleByIdHandler(Repository);
        Create = new CreateTournamentRuleHandler(Repository, Clock);
        Update = new UpdateTournamentRuleHandler(Repository, Clock);
        Delete = new DeleteTournamentRuleHandler(Repository);
    }

    public InMemoryTournamentRuleRepository Repository { get; }
    public TestClock Clock { get; }
    public GetTournamentRulesHandler GetAll { get; }
    public GetTournamentRuleByIdHandler GetById { get; }
    public CreateTournamentRuleHandler Create { get; }
    public UpdateTournamentRuleHandler Update { get; }
    public DeleteTournamentRuleHandler Delete { get; }
}
