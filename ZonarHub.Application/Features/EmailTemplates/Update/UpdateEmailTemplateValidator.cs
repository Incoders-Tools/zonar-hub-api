using FluentValidation;

namespace ZonarHub.Application.Features.EmailTemplates.Update;

internal sealed class UpdateEmailTemplateValidator : AbstractValidator<UpdateEmailTemplateCommand>
{
    public UpdateEmailTemplateValidator()
    {
        RuleFor(x => x.Id)
            .NotEqual(Guid.Empty)
            .WithMessage("email_templates.errors.id_required");

        RuleFor(x => x.Subject)
            .NotEmpty()
            .WithMessage("email_templates.errors.subject_required");

        RuleFor(x => x.HtmlBody)
            .NotEmpty()
            .WithMessage("email_templates.errors.html_body_required");
    }
}
