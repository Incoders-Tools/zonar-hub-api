using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZonarHub.Application.Abstractions;
using ZonarHub.Infrastructure.Email;

namespace ZonarHub.Infrastructure.Auth;

internal static class AuthInfrastructureExtensions
{
    internal static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Secret), $"{JwtOptions.SectionName}:Secret is required.")
            .ValidateOnStart();

        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName));

        services.AddSingleton<ISmtpClientAdapterFactory, MailKitSmtpClientAdapterFactory>();

        services.AddScoped<IEmailService>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<EmailOptions>>().Value;
            if (!opts.Provider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Unsupported email provider: '{opts.Provider}'. Supported provider: Smtp.");
            }

            return ActivatorUtilities.CreateInstance<SmtpEmailService>(sp);
        });

        services.AddScoped<IEmailTemplateComposer, EmailTemplateComposer>();

        return services;
    }
}
