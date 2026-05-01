using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Email;

/// <summary>
/// Production SMTP email service using MailKit.
/// Activate by setting Email:Provider = "Smtp" and configuring Email:Smtp section.
/// </summary>
internal sealed class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;

    public SmtpEmailService(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = message.HtmlBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            _options.Smtp.Host,
            _options.Smtp.Port,
            _options.Smtp.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.Smtp.Username))
            await client.AuthenticateAsync(_options.Smtp.Username, _options.Smtp.Password, cancellationToken);

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
