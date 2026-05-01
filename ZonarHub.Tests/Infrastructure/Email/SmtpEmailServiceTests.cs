using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using ZonarHub.Application.Abstractions;
using ZonarHub.Infrastructure.Email;

namespace ZonarHub.Tests.Infrastructure.Email;

public sealed class SmtpEmailServiceTests
{
    [Fact]
    public async Task SendAsync_WhenSuppressionEnabled_DoesNotThrowOnConnectionFailure()
    {
        var options = Options.Create(new EmailOptions
        {
            SuppressDeliveryFailures = true,
            FromAddress = "noreply@zonarhub.com",
            FromName = "ZonarHub",
            Smtp = new EmailOptions.SmtpSettings
            {
                Host = "smtp.invalid",
                Port = 1025,
                UseSsl = false,
            },
        });

        var service = new SmtpEmailService(
            options,
            new ThrowingFactory(new InvalidOperationException("SMTP unavailable")),
            NullLogger<SmtpEmailService>.Instance);

        await service.SendAsync(new EmailMessage("user@test.com", "Subject", "<p>Body</p>"));
    }

    [Fact]
    public async Task SendAsync_WhenSuppressionDisabled_ThrowsOnConnectionFailure()
    {
        var options = Options.Create(new EmailOptions
        {
            SuppressDeliveryFailures = false,
            FromAddress = "noreply@zonarhub.com",
            FromName = "ZonarHub",
            Smtp = new EmailOptions.SmtpSettings
            {
                Host = "smtp.invalid",
                Port = 1025,
                UseSsl = false,
            },
        });

        var service = new SmtpEmailService(
            options,
            new ThrowingFactory(new InvalidOperationException("SMTP unavailable")),
            NullLogger<SmtpEmailService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendAsync(new EmailMessage("user@test.com", "Subject", "<p>Body</p>")));
    }

    [Fact]
    public async Task SendAsync_WhenCredentialsPresent_AuthenticatesAndSends()
    {
        var adapter = new RecordingAdapter();

        var options = Options.Create(new EmailOptions
        {
            SuppressDeliveryFailures = false,
            FromAddress = "noreply@zonarhub.com",
            FromName = "ZonarHub",
            Smtp = new EmailOptions.SmtpSettings
            {
                Host = "localhost",
                Port = 1025,
                UseSsl = false,
                Username = "tester",
                Password = "secret",
            },
        });

        var service = new SmtpEmailService(
            options,
            new FixedFactory(adapter),
            NullLogger<SmtpEmailService>.Instance);

        await service.SendAsync(new EmailMessage("user@test.com", "Subject", "<p>Body</p>"));

        Assert.True(adapter.Connected);
        Assert.True(adapter.Authenticated);
        Assert.True(adapter.Sent);
        Assert.True(adapter.Disconnected);
    }

    private sealed class ThrowingFactory : ISmtpClientAdapterFactory
    {
        private readonly Exception _exception;

        public ThrowingFactory(Exception exception)
        {
            _exception = exception;
        }

        public ISmtpClientAdapter Create() => new ThrowingAdapter(_exception);
    }

    private sealed class ThrowingAdapter : ISmtpClientAdapter
    {
        private readonly Exception _exception;

        public ThrowingAdapter(Exception exception)
        {
            _exception = exception;
        }

        public Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
            => Task.FromException(_exception);

        public Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task DisconnectAsync(bool quit, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedFactory : ISmtpClientAdapterFactory
    {
        private readonly ISmtpClientAdapter _adapter;

        public FixedFactory(ISmtpClientAdapter adapter)
        {
            _adapter = adapter;
        }

        public ISmtpClientAdapter Create() => _adapter;
    }

    private sealed class RecordingAdapter : ISmtpClientAdapter
    {
        public bool Connected { get; private set; }
        public bool Authenticated { get; private set; }
        public bool Sent { get; private set; }
        public bool Disconnected { get; private set; }

        public Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
        {
            Connected = true;
            return Task.CompletedTask;
        }

        public Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
        {
            Authenticated = true;
            return Task.CompletedTask;
        }

        public Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
        {
            Sent = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(bool quit, CancellationToken cancellationToken)
        {
            Disconnected = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
