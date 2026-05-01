using FluentValidation;

namespace ZonarHub.Application.Features.Auth.ResetPassword;

internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Token).NotEmpty().WithMessage("auth.errors.token_required");
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).WithMessage("auth.errors.password_too_short");
    }
}
