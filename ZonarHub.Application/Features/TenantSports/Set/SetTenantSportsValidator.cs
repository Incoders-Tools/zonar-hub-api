using FluentValidation;

namespace ZonarHub.Application.Features.TenantSports.Set;

internal sealed class SetTenantSportsValidator : AbstractValidator<SetTenantSportsCommand>
{
    public SetTenantSportsValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEqual(Guid.Empty)
            .WithMessage("tenant_sports.errors.tenant_id_required");
    }
}
