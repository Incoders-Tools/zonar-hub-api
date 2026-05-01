using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.EmailTemplates;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryEmailTemplateRepository : IEmailTemplateRepository
{
    private readonly InMemoryEmailTemplateStore _store;

    public InMemoryEmailTemplateRepository(InMemoryEmailTemplateStore store)
    {
        _store = store;
    }

    public Task<EmailTemplate?> GetByIdAsync(EmailTemplateId id, CancellationToken cancellationToken = default)
    {
        _store.Templates.TryGetValue(id, out var template);
        return Task.FromResult(template);
    }

    public Task<EmailTemplate?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var match = _store.Templates.Values.FirstOrDefault(t =>
            string.Equals(t.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(match);
    }

    public Task<(IReadOnlyList<EmailTemplate> Items, int TotalCount)> ListAsync(
        EmailTemplateQuery query,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<EmailTemplate> source = _store.Templates.Values;

        if (!string.IsNullOrWhiteSpace(query.KeyContains))
        {
            var key = query.KeyContains.Trim();
            source = source.Where(t => t.Key.Contains(key, StringComparison.OrdinalIgnoreCase));
        }

        if (query.IsActive is { } isActive)
        {
            source = source.Where(t => t.IsActive == isActive);
        }

        var ordered = source
            .OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = ordered.Count;
        IReadOnlyList<EmailTemplate> page = ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return Task.FromResult((page, total));
    }

    public Task AddAsync(EmailTemplate template, CancellationToken cancellationToken = default)
    {
        if (!_store.Templates.TryAdd(template.Id, template))
        {
            throw new InvalidOperationException($"EmailTemplate '{template.Id}' already exists in memory.");
        }

        return Task.CompletedTask;
    }

    public void Update(EmailTemplate template)
    {
        _store.Templates[template.Id] = template;
    }
}
