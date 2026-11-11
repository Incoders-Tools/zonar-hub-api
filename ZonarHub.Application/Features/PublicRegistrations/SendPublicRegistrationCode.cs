using System.Net;
using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Registrations;

namespace ZonarHub.Application.Features.PublicRegistrations;

/// <summary>Emails a registration code for a showcase tournament to the contact address (S2, D4).</summary>
public sealed record SendPublicRegistrationCodeCommand(string Slug, PublicRegistrationCodeBody Body)
    : IRequest<Result<PublicRegistrationCodeResponse>>;

/// <remarks>
/// Order, nothing sent or cached on a failure: showcase tournament; open window (<see cref="PublicRegistrationErrors.Closed"/>);
/// contact email (<see cref="RegistrationErrors.ContactEmailRequired"/>, <see cref="RegistrationErrors.InvalidContactEmail"/>);
/// resend wait (<see cref="PublicRegistrationErrors.CodeCooldown"/>). The email is sent before the code is cached, so a
/// failed delivery leaves no code that blocks a retry. The answer does not depend on whether the address is a known
/// player.
/// </remarks>
public sealed class SendPublicRegistrationCodeHandler
    : IRequestHandler<SendPublicRegistrationCodeCommand, Result<PublicRegistrationCodeResponse>>
{
    private readonly PublicRegistrationScope _scope;
    private readonly PublicRegistrationCodes _codes;
    private readonly IEmailService _email;
    private readonly IEmailTemplateComposer _templates;
    private readonly IClock _clock;

    public SendPublicRegistrationCodeHandler(
        IPublicTournamentReadRepository showcase,
        ITournamentAdminRepository tournaments,
        ICacheStore cache,
        IEmailService email,
        IEmailTemplateComposer templates,
        IClock clock)
    {
        _scope = new PublicRegistrationScope(showcase, tournaments);
        _codes = new PublicRegistrationCodes(cache, clock);
        _email = email;
        _templates = templates;
        _clock = clock;
    }

    public async Task<Result<PublicRegistrationCodeResponse>> Handle(SendPublicRegistrationCodeCommand request, CancellationToken cancellationToken)
    {
        var loaded = await _scope.LoadAsync(request.Slug, cancellationToken);
        if (loaded.IsFailure)
            return Failure(loaded.Error);

        var tournament = loaded.Value;
        if (PublicRegistrationScope.WindowState(tournament, _clock.UtcNow) != RegistrationWindowState.Open)
            return Failure(PublicRegistrationErrors.Closed);

        var email = ContactEmail(request.Body.ContactEmail);
        if (email.IsFailure)
            return Failure(email.Error);

        if (!await _codes.CanSendAsync(tournament.Id, email.Value, cancellationToken))
            return Failure(PublicRegistrationErrors.CodeCooldown);

        var code = PublicRegistrationCodes.NewCode();
        var minutes = ((int)PublicRegistrationCodes.Lifetime.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var message = await _templates.ComposeAsync(
            email.Value,
            PublicRegistrationKeys.CodeEmailTemplate,
            "Tu código de inscripción - ZonarHub",
            $"""
            <h2>Inscripción a {WebUtility.HtmlEncode(tournament.Name)}</h2>
            <p>Tu código de inscripción es:</p>
            <h1 style="letter-spacing:4px">{code}</h1>
            <p>Válido por {minutes} minutos.</p>
            """,
            new Dictionary<string, string>
            {
                ["code"] = code,
                ["expires_minutes"] = minutes,
                ["tournament_name"] = tournament.Name,
            },
            cancellationToken);
        await _email.SendAsync(message, cancellationToken);
        await _codes.StoreAsync(tournament.Id, email.Value, code, cancellationToken);

        return Result.Success(new PublicRegistrationCodeResponse(
            (int)PublicRegistrationCodes.Lifetime.TotalSeconds,
            (int)PublicRegistrationCodes.ResendCooldown.TotalSeconds));
    }

    /// <summary>The normalized contact email (<see cref="Player.NormalizeEmail"/>) when present and well formed.</summary>
    internal static Result<string> ContactEmail(string? value)
    {
        var email = Player.NormalizeEmail(value);
        if (email is null)
            return Result.Failure<string>(RegistrationErrors.ContactEmailRequired);

        return Player.IsValidEmail(email) ? Result.Success(email) : Result.Failure<string>(RegistrationErrors.InvalidContactEmail);
    }

    private static Result<PublicRegistrationCodeResponse> Failure(Error error) => Result.Failure<PublicRegistrationCodeResponse>(error);
}
