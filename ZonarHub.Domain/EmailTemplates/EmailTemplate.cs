using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.EmailTemplates;

/// <summary>
/// System-level email template used by outbound flows such as verification and password reset.
/// </summary>
public sealed class EmailTemplate : Entity<EmailTemplateId>
{
    private EmailTemplate(
        EmailTemplateId id,
        string key,
        string subject,
        string htmlBody,
        string? description,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        Key = key;
        Subject = subject;
        HtmlBody = htmlBody;
        Description = description;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public string Key { get; private set; }

    public string Subject { get; private set; }

    public string HtmlBody { get; private set; }

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<EmailTemplate> Create(
        EmailTemplateId id,
        string key,
        string subject,
        string htmlBody,
        string? description,
        bool isActive,
        DateTime nowUtc)
    {
        var validation = Validate(key, subject, htmlBody);
        if (validation.IsFailure)
        {
            return Result.Failure<EmailTemplate>(validation.Error);
        }

        return Result.Success(new EmailTemplate(
            id,
            key.Trim().ToLowerInvariant(),
            subject.Trim(),
            htmlBody,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            isActive,
            nowUtc,
            nowUtc));
    }

    public static EmailTemplate Reconstitute(
        EmailTemplateId id,
        string key,
        string subject,
        string htmlBody,
        string? description,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc) =>
        new(
            id,
            key,
            subject,
            htmlBody,
            description,
            isActive,
            createdAtUtc,
            updatedAtUtc);

    public Result Update(
        string subject,
        string htmlBody,
        string? description,
        bool isActive,
        DateTime nowUtc)
    {
        var validation = Validate(Key, subject, htmlBody);
        if (validation.IsFailure)
        {
            return validation;
        }

        Subject = subject.Trim();
        HtmlBody = htmlBody;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }

    private static Result Validate(string key, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Failure(EmailTemplateErrors.KeyRequired);
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return Result.Failure(EmailTemplateErrors.SubjectRequired);
        }

        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            return Result.Failure(EmailTemplateErrors.HtmlBodyRequired);
        }

        return Result.Success();
    }
}
