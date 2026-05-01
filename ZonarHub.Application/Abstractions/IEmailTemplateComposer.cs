namespace ZonarHub.Application.Abstractions;

public interface IEmailTemplateComposer
{
    Task<EmailMessage> ComposeAsync(
        string to,
        string templateKey,
        string fallbackSubject,
        string fallbackHtmlBody,
        IReadOnlyDictionary<string, string>? variables = null,
        CancellationToken cancellationToken = default);
}
