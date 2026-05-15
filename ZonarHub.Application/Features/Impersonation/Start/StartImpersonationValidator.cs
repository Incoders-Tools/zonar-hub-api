using FluentValidation;

namespace ZonarHub.Application.Features.Impersonation.Start;

/// <summary>
/// FluentValidation rules for <see cref="StartImpersonationCommand"/>.
/// Satisfies: design §4.2.
/// </summary>
internal sealed class StartImpersonationValidator : AbstractValidator<StartImpersonationCommand>
{
    public StartImpersonationValidator()
    {
        RuleFor(x => x.TargetUserId)
            .NotEmpty()
            .WithMessage("admin.impersonation.errors.targetUserIdRequired");

        RuleFor(x => x.Reason)
            .MaximumLength(500)
            .WithMessage("admin.impersonation.errors.reasonTooLong")
            .When(x => x.Reason is not null);
    }
}
