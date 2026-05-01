using ZonarHub.Application.Features.EmailTemplates.Update;
using ZonarHub.Domain.EmailTemplates;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.EmailTemplates;

public sealed class EmailTemplateUpdateTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Update_WhenTemplateExists_UpdatesAndReturnsResponse()
    {
        var store = new InMemoryEmailTemplateStore();
        var repo = new InMemoryEmailTemplateRepository(store);
        var uow = new InMemoryUnitOfWork();
        var clock = new TestClock(Now);
        var handler = new UpdateEmailTemplateHandler(repo, uow, clock);

        var template = EmailTemplate.Create(
            EmailTemplateId.New(),
            "auth.verification_code",
            "Old subject",
            "<p>Old</p>",
            "old",
            true,
            Now).Value;

        await repo.AddAsync(template, CancellationToken.None);

        clock.UtcNow = Now.AddHours(1);
        var result = await handler.Handle(
            new UpdateEmailTemplateCommand(
                template.Id.Value,
                "New subject",
                "<p>New</p>",
                "updated",
                false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New subject", result.Value.Subject);
        Assert.Equal("<p>New</p>", result.Value.HtmlBody);
        Assert.False(result.Value.IsActive);
        Assert.Equal(Now.AddHours(1), result.Value.UpdatedAtUtc);
    }

    [Fact]
    public async Task Update_WhenTemplateIsMissing_ReturnsNotFound()
    {
        var repo = new InMemoryEmailTemplateRepository(new InMemoryEmailTemplateStore());
        var uow = new InMemoryUnitOfWork();
        var clock = new TestClock(Now);
        var handler = new UpdateEmailTemplateHandler(repo, uow, clock);

        var result = await handler.Handle(
            new UpdateEmailTemplateCommand(
                Guid.NewGuid(),
                "subject",
                "<p>body</p>",
                null,
                true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("email_templates.not_found", result.Error.Code);
    }
}
