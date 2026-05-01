using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.EmailTemplates;

public static class EmailTemplateErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "email_templates.not_found",
        "email_templates.errors.not_found");

    public static readonly Error KeyRequired = Error.Validation(
        "email_templates.key_required",
        "email_templates.errors.key_required");

    public static readonly Error SubjectRequired = Error.Validation(
        "email_templates.subject_required",
        "email_templates.errors.subject_required");

    public static readonly Error HtmlBodyRequired = Error.Validation(
        "email_templates.html_body_required",
        "email_templates.errors.html_body_required");
}
