using FluentValidation;

namespace ZonarHub.Application.Features.Auth.Register;

internal sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MinimumLength(2).WithMessage("auth.errors.full_name_required");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("auth.errors.email_invalid");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).WithMessage("auth.errors.password_too_short");
        RuleFor(x => x.VerificationCode).NotEmpty().Length(6).WithMessage("auth.errors.verification_code_invalid");
    }
}
