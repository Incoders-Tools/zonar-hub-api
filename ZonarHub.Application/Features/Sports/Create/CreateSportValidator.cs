using FluentValidation;

namespace ZonarHub.Application.Features.Sports.Create;

internal sealed class CreateSportValidator : AbstractValidator<CreateSportCommand>
{
    public CreateSportValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("sports.errors.name_required").MaximumLength(100);
        RuleFor(x => x.Key).NotEmpty().WithMessage("sports.errors.key_required").MaximumLength(50);
        RuleFor(x => x.Icon).NotEmpty().WithMessage("sports.errors.icon_required");
        RuleFor(x => x.IconSource).IsInEnum().WithMessage("sports.errors.icon_source_invalid");
    }
}
