using Microsoft.Extensions.Logging;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Email;

/// <summary>
/// Dev email service — logs messages to ILogger instead of sending real emails.
/// Activate by setting Email:Provider = "InMemory".
/// </summary>
internal sealed class InMemoryEmailService : IEmailService
{
    private readonly ILogger<InMemoryEmailService> _logger;

    public InMemoryEmailService(ILogger<InMemoryEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[EMAIL] To={To} Subject={Subject}\n{Body}",
            message.To,
            message.Subject,
            message.HtmlBody);

        return Task.CompletedTask;
    }
}
