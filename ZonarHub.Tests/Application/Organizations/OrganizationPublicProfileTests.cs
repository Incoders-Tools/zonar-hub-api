using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ZonarHub.Application;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Organizations.Create;
using ZonarHub.Application.Features.Organizations.GetById;
using ZonarHub.Application.Features.Organizations.Update;
using ZonarHub.Domain.Organizations;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.Organizations;

/// <summary>
/// Organization public-profile consent and social links: round-trip through create, update and read, and
/// input validation that rejects unsafe or malformed values before anything is stored.
/// </summary>
public class OrganizationPublicProfileTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task CreateUpdateAndRead_RoundTripConsentAndLinks()
    {
        var h = new OrganizationsTestHarness(Now);

        var created = await h.CreateOrg.Handle(
            CreateCommand() with
            {
                PublicProfileEnabled = true,
                WebsiteUrl = "https://club.example",
                InstagramUrl = "https://instagram.com/club",
                FacebookUrl = "https://facebook.com/club",
                TiktokUrl = "https://tiktok.com/@club",
                YoutubeUrl = "https://youtube.com/@club",
                WhatsappPhone = "+5491122334455",
                ContactEmail = "hola@club.example",
            },
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.True(created.Value.PublicProfileEnabled);
        Assert.Equal("https://club.example", created.Value.WebsiteUrl);
        Assert.Equal("https://instagram.com/club", created.Value.InstagramUrl);
        Assert.Equal("https://facebook.com/club", created.Value.FacebookUrl);
        Assert.Equal("https://tiktok.com/@club", created.Value.TiktokUrl);
        Assert.Equal("https://youtube.com/@club", created.Value.YoutubeUrl);
        Assert.Equal("+5491122334455", created.Value.WhatsappPhone);
        Assert.Equal("hola@club.example", created.Value.ContactEmail);

        var updated = await h.UpdateOrg.Handle(
            UpdateCommand(created.Value.Id) with
            {
                PublicProfileEnabled = false,
                WebsiteUrl = "http://new.example/path",
                InstagramUrl = null,
                WhatsappPhone = "12345678",
                ContactEmail = "team@club.example",
            },
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.False(updated.Value.PublicProfileEnabled);
        Assert.Equal("http://new.example/path", updated.Value.WebsiteUrl);
        Assert.Null(updated.Value.InstagramUrl);
        Assert.Null(updated.Value.FacebookUrl);
        Assert.Equal("12345678", updated.Value.WhatsappPhone);
        Assert.Equal("team@club.example", updated.Value.ContactEmail);

        var fetched = await h.GetOrgById.Handle(new GetOrganizationByIdQuery(created.Value.Id, TenantA), CancellationToken.None);

        Assert.Equal(updated.Value, fetched.Value);
    }

    [Fact]
    public async Task Create_WithoutProfileFields_DefaultsToNoConsentAndNoLinks()
    {
        var h = new OrganizationsTestHarness(Now);

        var created = await h.CreateOrg.Handle(CreateCommand(), CancellationToken.None);

        Assert.False(created.Value.PublicProfileEnabled);
        Assert.Null(created.Value.WebsiteUrl);
        Assert.Null(created.Value.InstagramUrl);
        Assert.Null(created.Value.FacebookUrl);
        Assert.Null(created.Value.TiktokUrl);
        Assert.Null(created.Value.YoutubeUrl);
        Assert.Null(created.Value.WhatsappPhone);
        Assert.Null(created.Value.ContactEmail);
    }

    [Fact]
    public async Task CreateAndUpdate_ThroughThePipeline_NormaliseBlankAndPaddedValues()
    {
        await using var app = PipelineApp.Create();

        var created = await app.Sender.Send(
            CreateCommand() with
            {
                WebsiteUrl = "",
                InstagramUrl = "   ",
                FacebookUrl = "  https://facebook.com/club  ",
                TiktokUrl = "",
                YoutubeUrl = " ",
                WhatsappPhone = "",
                ContactEmail = "  hola@club.example ",
            });

        Assert.True(created.IsSuccess);
        Assert.Null(created.Value.WebsiteUrl);
        Assert.Null(created.Value.InstagramUrl);
        Assert.Equal("https://facebook.com/club", created.Value.FacebookUrl);
        Assert.Null(created.Value.TiktokUrl);
        Assert.Null(created.Value.YoutubeUrl);
        Assert.Null(created.Value.WhatsappPhone);
        Assert.Equal("hola@club.example", created.Value.ContactEmail);

        var updated = await app.Sender.Send(
            UpdateCommand(created.Value.Id) with { FacebookUrl = "", ContactEmail = " ", WhatsappPhone = " +12345678 " });

        Assert.Null(updated.Value.FacebookUrl);
        Assert.Null(updated.Value.ContactEmail);
        Assert.Equal("+12345678", updated.Value.WhatsappPhone);
    }

    public static TheoryData<string, string> InvalidValues => new()
    {
        { "WebsiteUrl", "javascript:alert(1)" },
        { "WebsiteUrl", "ftp://club.example/file" },
        { "WebsiteUrl", "club.example" },
        { "WebsiteUrl", "/relative/path" },
        { "WebsiteUrl", "https://club.example/" + new string('a', 481) },
        { "InstagramUrl", "javascript:alert(1)" },
        { "FacebookUrl", "data:text/html,hi" },
        { "TiktokUrl", "ftp://tiktok.com/club" },
        { "YoutubeUrl", "mailto:club@example.com" },
        { "ContactEmail", "not-an-email" },
        { "ContactEmail", "a@b" },
        { "ContactEmail", new string('a', 250) + "@x.io" },
        { "WhatsappPhone", "+54 9 11 2233" },
        { "WhatsappPhone", "54911abc4455" },
        { "WhatsappPhone", "+1234567" },
        { "WhatsappPhone", "+12345678901234567" },
        { "WhatsappPhone", "++12345678" },
    };

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public async Task Create_InvalidProfileValue_IsRejectedWithoutStoringAnything(string field, string value)
    {
        await using var app = PipelineApp.Create();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => app.Sender.Send(WithField(CreateCommand(), field, value)));

        Assert.Contains(error.Errors, failure => failure.PropertyName == field);
        Assert.Empty(app.Store.Data);
    }

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public async Task Update_InvalidProfileValue_IsRejectedWithoutChangingTheOrganization(string field, string value)
    {
        await using var app = PipelineApp.Create();
        var created = await app.Sender.Send(CreateCommand() with { WebsiteUrl = "https://club.example" });

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => app.Sender.Send(WithField(UpdateCommand(created.Value.Id), field, value)));

        Assert.Contains(error.Errors, failure => failure.PropertyName == field);
        var stored = app.Store.Data[new OrganizationId(created.Value.Id)];
        Assert.Equal("https://club.example", stored.PublicProfile.WebsiteUrl);
        Assert.Equal("Padel Club", stored.DisplayName);
        Assert.Equal(Now, stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task Create_ValidBoundaryValues_AreAccepted()
    {
        await using var app = PipelineApp.Create();
        var url = "https://club.example/" + new string('a', 500 - "https://club.example/".Length);
        var email = new string('a', 254 - "@club.io".Length) + "@club.io";

        var created = await app.Sender.Send(CreateCommand() with
        {
            WebsiteUrl = url,
            ContactEmail = email,
            WhatsappPhone = "+1234567890123456",
        });

        Assert.True(created.IsSuccess);
        Assert.Equal(url, created.Value.WebsiteUrl);
        Assert.Equal(email, created.Value.ContactEmail);
        Assert.Equal("+1234567890123456", created.Value.WhatsappPhone);
    }

    [Fact]
    public void DomainUpdate_WithoutAProfile_KeepsTheCurrentProfile()
    {
        var org = Organization.Create(
            OrganizationId.New(), TenantA, "Club", null, null, OrganizationType.Estandar, null, UserId, Now,
            OrganizationPublicProfile.Create(true, "https://club.example", null, null, null, null, null, null)).Value;

        var result = org.Update("Club", null, null, OrganizationType.Estandar, null, false, Now.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.True(org.PublicProfile.Enabled);
        Assert.Equal("https://club.example", org.PublicProfile.WebsiteUrl);
    }

    private static CreateOrganizationCommand CreateCommand() =>
        new(TenantA, "Padel Club", null, null, OrganizationType.Circuito, null, UserId);

    private static UpdateOrganizationCommand UpdateCommand(Guid id) =>
        new(id, "Padel Club", null, null, OrganizationType.Circuito, null, true, TenantA);

    private static T WithField<T>(T command, string field, string value)
    {
        var copy = command switch
        {
            CreateOrganizationCommand create => (object)(create with { }),
            UpdateOrganizationCommand update => update with { },
            _ => throw new NotSupportedException(),
        };
        typeof(T).GetProperty(field)!.SetValue(copy, value);
        return (T)copy;
    }

    /// <summary>
    /// Real MediatR pipeline (validators included) over the in-memory adapter, so a rejected request is
    /// observable as an unchanged store.
    /// </summary>
    private sealed class PipelineApp : IAsyncDisposable
    {
        private readonly ServiceProvider _root;
        private readonly AsyncServiceScope _scope;

        private PipelineApp(ServiceProvider root)
        {
            _root = root;
            _scope = root.CreateAsyncScope();
            Sender = _scope.ServiceProvider.GetRequiredService<ISender>();
            Store = _scope.ServiceProvider.GetRequiredService<InMemoryOrganizationStore>();
        }

        public ISender Sender { get; }

        public InMemoryOrganizationStore Store { get; }

        public static PipelineApp Create()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddApplication();
            services.AddInMemoryPersistence();
            services.AddSingleton<IClock>(new TestClock(Now));
            return new PipelineApp(services.BuildServiceProvider());
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _root.DisposeAsync();
        }
    }
}
