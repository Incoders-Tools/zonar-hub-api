using FluentValidation;

namespace ZonarHub.Application.Features.SystemSettings.Update;

public sealed class UpdateSystemSettingValidator : AbstractValidator<UpdateSystemSettingCommand>
{
    public UpdateSystemSettingValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty).WithMessage("system_settings.errors.id_required");
        RuleFor(x => x.Value).NotEmpty().WithMessage("system_settings.errors.value_required");
        RuleFor(x => x.Scope).IsInEnum().WithMessage("system_settings.errors.scope_invalid");
    }
}
