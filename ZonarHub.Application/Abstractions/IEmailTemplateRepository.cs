using ZonarHub.Domain.EmailTemplates;

namespace ZonarHub.Application.Abstractions;

public interface IEmailTemplateRepository
{
    Task<EmailTemplate?> GetByIdAsync(EmailTemplateId id, CancellationToken cancellationToken = default);

    Task<EmailTemplate?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<EmailTemplate> Items, int TotalCount)> ListAsync(
        EmailTemplateQuery query,
        CancellationToken cancellationToken = default);

    Task AddAsync(EmailTemplate template, CancellationToken cancellationToken = default);

    void Update(EmailTemplate template);
}

public sealed record EmailTemplateQuery(
    string? KeyContains,
    bool? IsActive,
    int Page,
    int PageSize);
