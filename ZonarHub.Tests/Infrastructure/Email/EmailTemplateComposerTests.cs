using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.EmailTemplates;
using ZonarHub.Infrastructure.Email;
using ZonarHub.Infrastructure.Persistence.InMemory;

namespace ZonarHub.Tests.Infrastructure.Email;

public sealed class EmailTemplateComposerTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ComposeAsync_UsesTemplateAndReplacesTokens()
    {
        var repo = new InMemoryEmailTemplateRepository(new InMemoryEmailTemplateStore());
        var template = EmailTemplate.Create(
            EmailTemplateId.New(),
            "auth.welcome",
            "Hola {{full_name}}",
            "<p>Bienvenido {{full_name}}</p>",
            null,
            true,
            Now).Value;

        await repo.AddAsync(template, CancellationToken.None);

        IEmailTemplateComposer composer = new EmailTemplateComposer(repo);

        var message = await composer.ComposeAsync(
            "test@example.com",
            "auth.welcome",
            "Fallback subject",
            "<p>Fallback body</p>",
            new Dictionary<string, string>
            {
                ["full_name"] = "Patricio"
            },
            CancellationToken.None);

        Assert.Equal("Hola Patricio", message.Subject);
        Assert.Equal("<p>Bienvenido Patricio</p>", message.HtmlBody);
    }

    [Fact]
    public async Task ComposeAsync_WhenTemplateMissing_UsesFallback()
    {
        var repo = new InMemoryEmailTemplateRepository(new InMemoryEmailTemplateStore());
        IEmailTemplateComposer composer = new EmailTemplateComposer(repo);

        var message = await composer.ComposeAsync(
            "test@example.com",
            "auth.unknown",
            "Fallback subject",
            "<p>Fallback body</p>",
            null,
            CancellationToken.None);

        Assert.Equal("Fallback subject", message.Subject);
        Assert.Equal("<p>Fallback body</p>", message.HtmlBody);
    }
}
