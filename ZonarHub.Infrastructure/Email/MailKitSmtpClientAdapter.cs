using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ZonarHub.Infrastructure.Email;

internal sealed class MailKitSmtpClientAdapter : ISmtpClientAdapter
{
    private readonly SmtpClient _client = new();

    public Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
        => _client.ConnectAsync(
            host,
            port,
            useSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            cancellationToken);

    public Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
        => _client.AuthenticateAsync(username, password, cancellationToken);

    public Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
        => _client.SendAsync(message, cancellationToken);

    public Task DisconnectAsync(bool quit, CancellationToken cancellationToken)
        => _client.DisconnectAsync(quit, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class MailKitSmtpClientAdapterFactory : ISmtpClientAdapterFactory
{
    public ISmtpClientAdapter Create() => new MailKitSmtpClientAdapter();
}
