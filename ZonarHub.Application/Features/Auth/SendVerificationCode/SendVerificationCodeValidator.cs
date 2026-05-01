using FluentValidation;

namespace ZonarHub.Application.Features.Auth.SendVerificationCode;

internal sealed class SendVerificationCodeValidator : AbstractValidator<SendVerificationCodeCommand>
{
    public SendVerificationCodeValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("auth.errors.email_invalid");
    }
}
