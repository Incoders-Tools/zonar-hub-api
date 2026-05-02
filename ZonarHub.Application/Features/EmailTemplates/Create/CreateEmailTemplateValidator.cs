using FluentValidation;

namespace ZonarHub.Application.Features.EmailTemplates.Create;

internal sealed class CreateEmailTemplateValidator : AbstractValidator<CreateEmailTemplateCommand>
{
    public CreateEmailTemplateValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty()
            .WithMessage("Key is required");

        RuleFor(x => x.Subject)
            .NotEmpty()
            .WithMessage("Subject is required");

        RuleFor(x => x.HtmlBody)
            .NotEmpty()
            .WithMessage("HTML body is required");
    }
}
