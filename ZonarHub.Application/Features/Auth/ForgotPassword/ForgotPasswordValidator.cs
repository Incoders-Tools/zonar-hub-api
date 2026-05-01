using FluentValidation;

namespace ZonarHub.Application.Features.Auth.ForgotPassword;

internal sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("auth.errors.email_invalid");
    }
}
