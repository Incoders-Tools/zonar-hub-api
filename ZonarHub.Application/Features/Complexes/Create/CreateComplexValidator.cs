using FluentValidation;

namespace ZonarHub.Application.Features.Complexes.Create;

internal sealed class CreateComplexValidator : AbstractValidator<CreateComplexCommand>
{
    public CreateComplexValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().WithMessage("complexes.errors.organization_id_required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("complexes.errors.name_required").MaximumLength(200);
        RuleFor(x => x.Address).NotEmpty().WithMessage("complexes.errors.address_required").MaximumLength(400);
        RuleFor(x => x.Location).MaximumLength(500).When(x => x.Location is not null);
    }
}
