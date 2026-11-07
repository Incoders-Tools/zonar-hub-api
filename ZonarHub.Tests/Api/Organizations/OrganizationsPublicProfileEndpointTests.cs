using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ZonarHub.ApiService.Endpoints.Admin.System.Organizations;
using ZonarHub.Application.Features.Organizations;
using ZonarHub.Application.Features.Organizations.Create;
using ZonarHub.Application.Features.Organizations.Update;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Tests.Api.Organizations;

/// <summary>
/// The organization endpoints forward the public-profile consent and links from the flat request body.
/// </summary>
public class OrganizationsPublicProfileEndpointTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_ForwardsConsentAndLinks()
    {
        var sender = new CapturingSender();
        var body = new CreateOrganizationRequest(
            Guid.NewGuid(), "Club", null, null, "estandar", null, Guid.NewGuid(),
            PublicProfileEnabled: true,
            WebsiteUrl: "https://club.example",
            InstagramUrl: "https://instagram.com/club",
            FacebookUrl: "https://facebook.com/club",
            TiktokUrl: "https://tiktok.com/@club",
            YoutubeUrl: "https://youtube.com/@club",
            WhatsappPhone: "+5491122334455",
            ContactEmail: "hola@club.example");

        var result = await OrganizationsEndpointsExtensions.CreateAsync(body, new DefaultHttpContext(), sender, CancellationToken.None);

        Assert.IsType<Created<OrganizationResponse>>(result);
        var command = Assert.IsType<CreateOrganizationCommand>(sender.Received);
        Assert.True(command.PublicProfileEnabled);
        Assert.Equal("https://club.example", command.WebsiteUrl);
        Assert.Equal("https://instagram.com/club", command.InstagramUrl);
        Assert.Equal("https://facebook.com/club", command.FacebookUrl);
        Assert.Equal("https://tiktok.com/@club", command.TiktokUrl);
        Assert.Equal("https://youtube.com/@club", command.YoutubeUrl);
        Assert.Equal("+5491122334455", command.WhatsappPhone);
        Assert.Equal("hola@club.example", command.ContactEmail);
    }

    [Fact]
    public async Task Update_ForwardsConsentAndLinks()
    {
        var sender = new CapturingSender();
        var body = new UpdateOrganizationRequest(
            "Club", null, null, "estandar", null, true,
            PublicProfileEnabled: true,
            WebsiteUrl: "https://club.example",
            InstagramUrl: null,
            FacebookUrl: null,
            TiktokUrl: null,
            YoutubeUrl: "https://youtube.com/@club",
            WhatsappPhone: "+5491122334455",
            ContactEmail: "hola@club.example");

        var result = await OrganizationsEndpointsExtensions.UpdateAsync(
            Guid.NewGuid(), body, new DefaultHttpContext(), sender, CancellationToken.None);

        Assert.IsType<Ok<OrganizationResponse>>(result);
        var command = Assert.IsType<UpdateOrganizationCommand>(sender.Received);
        Assert.True(command.PublicProfileEnabled);
        Assert.Equal("https://club.example", command.WebsiteUrl);
        Assert.Null(command.InstagramUrl);
        Assert.Equal("https://youtube.com/@club", command.YoutubeUrl);
        Assert.Equal("+5491122334455", command.WhatsappPhone);
        Assert.Equal("hola@club.example", command.ContactEmail);
    }

    [Fact]
    public void Requests_OmittingProfileFields_DefaultToNoConsentAndNoLinks()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var update = JsonSerializer.Deserialize<UpdateOrganizationRequest>(
            """{"displayName":"Club","type":"estandar","isActive":true}""", options)!;

        Assert.False(update.PublicProfileEnabled);
        Assert.Null(update.WebsiteUrl);
        Assert.Null(update.ContactEmail);
    }

    [Fact]
    public void Response_SerializesTheDocumentedFieldNames()
    {
        var org = Organization.Create(
            OrganizationId.New(), Guid.NewGuid(), "Club", null, null, OrganizationType.Estandar, null, Guid.NewGuid(), Now,
            OrganizationPublicProfile.Create(true, "https://club.example", null, null, null, null, "+12345678", "a@b.io")).Value;

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            OrganizationResponse.FromDomain(org), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = json.RootElement;

        Assert.True(root.GetProperty("publicProfileEnabled").GetBoolean());
        Assert.Equal("https://club.example", root.GetProperty("websiteUrl").GetString());
        foreach (var name in new[] { "instagramUrl", "facebookUrl", "tiktokUrl", "youtubeUrl" })
        {
            Assert.Equal(JsonValueKind.Null, root.GetProperty(name).ValueKind);
        }

        Assert.Equal("+12345678", root.GetProperty("whatsappPhone").GetString());
        Assert.Equal("a@b.io", root.GetProperty("contactEmail").GetString());
    }

    private sealed class CapturingSender : ISender
    {
        public object? Received { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Received = request;
            var org = Organization.Create(
                OrganizationId.New(), Guid.NewGuid(), "Club", null, null, OrganizationType.Estandar, null, Guid.NewGuid(), Now).Value;
            object result = Result.Success(OrganizationResponse.FromDomain(org));
            return Task.FromResult((TResponse)result);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
