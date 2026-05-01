using FluentValidation;

namespace ZonarHub.Application.Features.SocialNetworks.Create;

internal sealed class CreateSocialNetworkValidator : AbstractValidator<CreateSocialNetworkCommand>
{
    public CreateSocialNetworkValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("social_networks.errors.name_required").MaximumLength(100);
        RuleFor(x => x.Key).NotEmpty().WithMessage("social_networks.errors.key_required").MaximumLength(50);
        RuleFor(x => x.Url)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Url))
            .WithMessage("social_networks.errors.url_invalid");
    }
}
