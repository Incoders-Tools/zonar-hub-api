using FluentValidation;

namespace ZonarHub.Application.Features.Complexes.Update;

internal sealed class UpdateComplexValidator : AbstractValidator<UpdateComplexCommand>
{
    public UpdateComplexValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("complexes.errors.id_required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("complexes.errors.name_required").MaximumLength(200);
        RuleFor(x => x.Address).NotEmpty().WithMessage("complexes.errors.address_required").MaximumLength(400);
        RuleFor(x => x.Location).MaximumLength(500).When(x => x.Location is not null);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
    }
}
