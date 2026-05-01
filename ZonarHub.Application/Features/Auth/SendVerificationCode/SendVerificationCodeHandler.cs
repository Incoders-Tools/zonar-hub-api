using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Auth;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Users;
using MediatR;

namespace ZonarHub.Application.Features.Auth.SendVerificationCode;

public sealed class SendVerificationCodeHandler : IRequestHandler<SendVerificationCodeCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly ICacheStore _cache;
    private readonly IEmailService _email;
    private readonly IEmailTemplateComposer _templates;
    private readonly IClock _clock;

    public SendVerificationCodeHandler(
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

    public async Task<Result> Handle(SendVerificationCodeCommand request, CancellationToken cancellationToken)
    {
        var emailLower = request.Email.Trim().ToLowerInvariant();

        if (await _users.ExistsByEmailAsync(emailLower, cancellationToken))
            return Result.Failure(UserErrors.EmailAlreadyExists);

        var code = GenerateCode();
        await _cache.SetAsync(CacheKey(emailLower), code, TimeSpan.FromMinutes(15), cancellationToken);

        var message = await _templates.ComposeAsync(
            emailLower,
            AuthEmailTemplateKeys.VerificationCode,
            "Tu código de verificación - ZonarHub",
            $"""
            <h2>Verificación de cuenta</h2>
            <p>Tu código de verificación es:</p>
            <h1 style="letter-spacing:4px">{code}</h1>
            <p>Válido por 15 minutos.</p>
            """,
            new Dictionary<string, string>
            {
                ["code"] = code,
                ["expires_minutes"] = "15",
            },
            cancellationToken);

        await _email.SendAsync(message, cancellationToken);

        return Result.Success();
    }

    internal static string CacheKey(string email) => $"verify:{email}";

    private static string GenerateCode() =>
        Random.Shared.Next(100000, 999999).ToString();
}
