using ZonarHub.Domain.EmailTemplates;

namespace ZonarHub.Tests.Domain.EmailTemplates;

public sealed class EmailTemplateTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidValues_Succeeds()
    {
        var result = EmailTemplate.Create(
            EmailTemplateId.New(),
            "auth.verification_code",
            "Subject",
            "<p>Hello</p>",
            "desc",
            true,
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("auth.verification_code", result.Value.Key);
        Assert.Equal("Subject", result.Value.Subject);
        Assert.True(result.Value.IsActive);
    }

    [Theory]
    [InlineData("", "S", "<p>x</p>", "email_templates.key_required")]
    [InlineData("auth.welcome", "", "<p>x</p>", "email_templates.subject_required")]
    [InlineData("auth.welcome", "S", "", "email_templates.html_body_required")]
    public void Create_WithInvalidInput_ReturnsExpectedError(
        string key,
        string subject,
        string html,
        string expectedErrorCode)
    {
        var result = EmailTemplate.Create(
            EmailTemplateId.New(),
            key,
            subject,
            html,
            null,
            true,
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedErrorCode, result.Error.Code);
    }

    [Fact]
    public void Update_ChangesTemplateAndUpdatedAt()
    {
        var template = EmailTemplate.Create(
            EmailTemplateId.New(),
            "auth.welcome",
            "Welcome",
            "<p>Hello</p>",
            null,
            true,
            Now).Value;

        var later = Now.AddHours(2);
        var result = template.Update("New subject", "<p>Updated</p>", "new description", false, later);

        Assert.True(result.IsSuccess);
        Assert.Equal("New subject", template.Subject);
        Assert.Equal("<p>Updated</p>", template.HtmlBody);
        Assert.Equal("new description", template.Description);
        Assert.False(template.IsActive);
        Assert.Equal(later, template.UpdatedAtUtc);
    }
}
