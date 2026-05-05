using FluentValidation;

namespace ZonarHub.Application.Features.UserPreferences.Set;

public sealed class SetUserPreferenceValidator : AbstractValidator<SetUserPreferenceCommand>
{
    public SetUserPreferenceValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required.");

        RuleFor(x => x.Key)
            .NotEmpty()
            .WithMessage("Preference key is required.")
            .MaximumLength(160)
            .WithMessage("Preference key cannot exceed 160 characters.");

        RuleFor(x => x.Value)
            .NotNull()
            .WithMessage("Preference value is required.");
    }
}
