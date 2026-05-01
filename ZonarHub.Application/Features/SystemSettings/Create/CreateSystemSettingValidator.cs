using FluentValidation;

namespace ZonarHub.Application.Features.SystemSettings.Create;

public sealed class CreateSystemSettingValidator : AbstractValidator<CreateSystemSettingCommand>
{
    public const int KeyMaxLength = 200;

    public CreateSystemSettingValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("system_settings.errors.key_required")
            .MaximumLength(KeyMaxLength).WithMessage("system_settings.errors.key_too_long");

        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("system_settings.errors.value_required");

        RuleFor(x => x.Scope)
            .IsInEnum().WithMessage("system_settings.errors.scope_invalid");
    }
}
