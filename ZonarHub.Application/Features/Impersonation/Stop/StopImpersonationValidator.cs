using FluentValidation;

namespace ZonarHub.Application.Features.Impersonation.Stop;

/// <summary>
/// FluentValidation rules for <see cref="StopImpersonationCommand"/>.
/// Satisfies: design §4.2.
/// </summary>
internal sealed class StopImpersonationValidator : AbstractValidator<StopImpersonationCommand>
{
    public StopImpersonationValidator()
    {
        RuleFor(x => x.SessionId)
            .NotEmpty()
            .WithMessage("admin.impersonation.errors.sessionIdRequired");
    }
}
