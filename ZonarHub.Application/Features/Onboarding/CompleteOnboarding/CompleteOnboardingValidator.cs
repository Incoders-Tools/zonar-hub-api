using FluentValidation;

namespace ZonarHub.Application.Features.Onboarding.CompleteOnboarding;

internal sealed class CompleteOnboardingValidator : AbstractValidator<CompleteOnboardingCommand>
{
    public CompleteOnboardingValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEqual(Guid.Empty)
            .WithMessage("onboarding.errors.tenant_id_required");

        RuleFor(x => x.CreatedByUserId)
            .NotEqual(Guid.Empty)
            .WithMessage("onboarding.errors.user_id_required");

        RuleFor(x => x.OrganizationDisplayName)
            .NotEmpty().WithMessage("onboarding.errors.org_name_required")
            .MinimumLength(2).WithMessage("onboarding.errors.org_name_too_short")
            .MaximumLength(200).WithMessage("onboarding.errors.org_name_too_long");

        RuleFor(x => x.OrganizationType)
            .IsInEnum()
            .WithMessage("onboarding.errors.org_type_invalid");

        RuleFor(x => x.EnabledSportIds)
            .NotNull().WithMessage("onboarding.errors.sports_required")
            .Must(ids => ids.Any()).WithMessage("onboarding.errors.sports_at_least_one");

        When(x => x.Venue is not null, () =>
        {
            RuleFor(x => x.Venue!.Name)
                .NotEmpty().WithMessage("onboarding.errors.venue_name_required")
                .MinimumLength(3).WithMessage("onboarding.errors.venue_name_too_short");

            RuleFor(x => x.Venue!.Address)
                .NotEmpty().WithMessage("onboarding.errors.venue_address_required");

            RuleFor(x => x.Venue!.CourtNames)
                .NotNull().WithMessage("onboarding.errors.court_names_required")
                .Must(names => names.Any()).WithMessage("onboarding.errors.court_names_at_least_one");
        });

        When(x => x.Tournament is not null, () =>
        {
            RuleFor(x => x.Tournament!.Name)
                .NotEmpty().WithMessage("onboarding.errors.tournament_name_required")
                .MinimumLength(3).WithMessage("onboarding.errors.tournament_name_too_short");

            RuleFor(x => x.Tournament!.StartDate)
                .LessThanOrEqualTo(x => x.Tournament!.EndDate)
                .WithMessage("onboarding.errors.tournament_invalid_date_range");
        });
    }
}
