using MimeKit;

namespace ZonarHub.Infrastructure.Email;

internal interface ISmtpClientAdapter : IAsyncDisposable
{
    Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken);
    Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken);
    Task SendAsync(MimeMessage message, CancellationToken cancellationToken);
    Task DisconnectAsync(bool quit, CancellationToken cancellationToken);
}

internal interface ISmtpClientAdapterFactory
{
    ISmtpClientAdapter Create();
}
