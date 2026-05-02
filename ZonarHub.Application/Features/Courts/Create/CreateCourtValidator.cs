using FluentValidation;

namespace ZonarHub.Application.Features.Courts.Create;

internal sealed class CreateCourtValidator : AbstractValidator<CreateCourtCommand>
{
    public CreateCourtValidator()
    {
        RuleFor(x => x.ComplexId)
            .NotEmpty()
            .WithMessage("courts.validation.complex_id_required");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("courts.validation.name_required")
            .MaximumLength(100)
            .WithMessage("courts.validation.name_max_length");
    }
}
