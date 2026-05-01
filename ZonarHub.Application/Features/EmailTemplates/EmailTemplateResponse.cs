using ZonarHub.Domain.EmailTemplates;

namespace ZonarHub.Application.Features.EmailTemplates;

public sealed record EmailTemplateResponse(
    Guid Id,
    string Key,
    string Subject,
    string HtmlBody,
    string? Description,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static EmailTemplateResponse FromDomain(EmailTemplate template) =>
        new(
            template.Id.Value,
            template.Key,
            template.Subject,
            template.HtmlBody,
            template.Description,
            template.IsActive,
            template.CreatedAtUtc,
            template.UpdatedAtUtc);
}
