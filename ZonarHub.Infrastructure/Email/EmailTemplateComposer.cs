using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Email;

internal sealed partial class EmailTemplateComposer : IEmailTemplateComposer
{
    private readonly IEmailTemplateRepository _templates;

    public EmailTemplateComposer(IEmailTemplateRepository templates)
    {
        _templates = templates;
    }

    public async Task<EmailMessage> ComposeAsync(
        string to,
        string templateKey,
        string fallbackSubject,
        string fallbackHtmlBody,
        IReadOnlyDictionary<string, string>? variables = null,
        CancellationToken cancellationToken = default)
    {
        ZonarHub.Domain.EmailTemplates.EmailTemplate? template;
        try
        {
            template = await _templates.GetByKeyAsync(templateKey, cancellationToken);
        }
        catch
        {
            // If storage is unavailable or migration has not run yet, keep auth flows alive.
            return new EmailMessage(to, fallbackSubject, fallbackHtmlBody);
        }

        if (template is null || !template.IsActive)
        {
            return new EmailMessage(to, fallbackSubject, fallbackHtmlBody);
        }

        var normalizedVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (variables is not null)
        {
            foreach (var (key, value) in variables)
            {
                normalizedVariables[key] = value;
            }
        }

        var subject = ReplaceTokens(template.Subject, normalizedVariables, encodeHtml: false);
        var htmlBody = ReplaceTokens(template.HtmlBody, normalizedVariables, encodeHtml: true);

        return new EmailMessage(to, subject, htmlBody);
    }

    private static string ReplaceTokens(
        string value,
        IReadOnlyDictionary<string, string> variables,
        bool encodeHtml)
    {
        return TokenRegex().Replace(value, match =>
        {
            var tokenName = match.Groups[1].Value;
            if (!variables.TryGetValue(tokenName, out var replacement))
            {
                return match.Value;
            }

            return encodeHtml
                ? HtmlEncoder.Default.Encode(replacement)
                : replacement;
        });
    }

    [GeneratedRegex("\\{\\{\\s*([a-zA-Z0-9_]+)\\s*\\}\\}", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();
}
