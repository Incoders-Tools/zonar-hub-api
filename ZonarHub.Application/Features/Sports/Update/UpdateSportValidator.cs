using FluentValidation;

namespace ZonarHub.Application.Features.Sports.Update;

internal sealed class UpdateSportValidator : AbstractValidator<UpdateSportCommand>
{
    public UpdateSportValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty).WithMessage("sports.errors.id_required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("sports.errors.name_required").MaximumLength(100);
        RuleFor(x => x.Icon).NotEmpty().WithMessage("sports.errors.icon_required");
        RuleFor(x => x.IconSource).IsInEnum().WithMessage("sports.errors.icon_source_invalid");
    }
}
