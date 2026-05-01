using FluentValidation;

namespace ZonarHub.Application.Features.OrganizationSports.Set;

internal sealed class SetOrganizationSportsValidator : AbstractValidator<SetOrganizationSportsCommand>
{
    public SetOrganizationSportsValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEqual(Guid.Empty)
            .WithMessage("organization_sports.errors.organization_id_required");
    }
}
