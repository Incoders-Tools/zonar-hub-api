namespace ZonarHub.Domain.EmailTemplates;

public readonly record struct EmailTemplateId(Guid Value)
{
    public static EmailTemplateId New() => new(Guid.NewGuid());
}
