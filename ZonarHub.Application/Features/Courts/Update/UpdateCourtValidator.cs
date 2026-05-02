using FluentValidation;

namespace ZonarHub.Application.Features.Courts.Update;

internal sealed class UpdateCourtValidator : AbstractValidator<UpdateCourtCommand>
{
    public UpdateCourtValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("courts.validation.id_required");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("courts.validation.name_required")
            .MaximumLength(100)
            .WithMessage("courts.validation.name_max_length");
    }
}
