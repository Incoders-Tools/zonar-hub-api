using System.Collections.Concurrent;
using ZonarHub.Domain.EmailTemplates;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryEmailTemplateStore
{
    private readonly ConcurrentDictionary<EmailTemplateId, EmailTemplate> _templates = new();

    internal ConcurrentDictionary<EmailTemplateId, EmailTemplate> Templates => _templates;
}
