using FluentValidation;

namespace ZonarHub.Application.Features.SocialNetworks.Update;

internal sealed class UpdateSocialNetworkValidator : AbstractValidator<UpdateSocialNetworkCommand>
{
    public UpdateSocialNetworkValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty).WithMessage("social_networks.errors.id_required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("social_networks.errors.name_required").MaximumLength(100);
        RuleFor(x => x.Url)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Url))
            .WithMessage("social_networks.errors.url_invalid");
    }
}
