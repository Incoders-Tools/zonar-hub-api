using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Auth;
using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Auth.ForgotPassword;

public sealed class ForgotPasswordHandler : IRequestHandler<ForgotPasswordCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly ICacheStore _cache;
    private readonly IEmailService _email;
    private readonly IEmailTemplateComposer _templates;
    private readonly IClock _clock;

    public ForgotPasswordHandler(
        IUserRepository users,
        ICacheStore cache,
        IEmailService email,
        IEmailTemplateComposer templates,
        IClock clock)
    {
        _users = users;
        _cache = cache;
        _email = email;
        _templates = templates;
        _clock = clock;
    }

    public async Task<Result> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var emailLower = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(emailLower, cancellationToken);

        // Always return success to avoid user enumeration
        if (user is null || !user.IsActive)
            return Result.Success();

        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await _cache.SetAsync(ResetCacheKey(token), emailLower, TimeSpan.FromHours(1), cancellationToken);

        var resetLink = $"{request.ResetUrlBase.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}";

        var message = await _templates.ComposeAsync(
            emailLower,
            AuthEmailTemplateKeys.PasswordReset,
            "Restablecer contraseña - ZonarHub",
            $"""
            <h2>Restablecer contraseña</h2>
            <p>Recibimos una solicitud para restablecer tu contraseña.</p>
            <p><a href="{resetLink}">Haz clic aquí para restablecer tu contraseña</a></p>
            <p>El enlace es válido por 1 hora.</p>
            <p>Si no solicitaste este cambio, puedes ignorar este email.</p>
            """,
            new Dictionary<string, string>
            {
                ["reset_link"] = resetLink,
                ["expires_hours"] = "1",
            },
            cancellationToken);

        await _email.SendAsync(message, cancellationToken);

        return Result.Success();
    }

    internal static string ResetCacheKey(string token) => $"pwd_reset:{token}";
}
