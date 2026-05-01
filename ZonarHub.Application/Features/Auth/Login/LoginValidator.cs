using FluentValidation;

namespace ZonarHub.Application.Features.Auth.Login;

internal sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("auth.errors.email_invalid");
        RuleFor(x => x.Password).NotEmpty().WithMessage("auth.errors.password_required");
    }
}
