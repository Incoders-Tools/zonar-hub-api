using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.Auth.CheckCode;
using ZonarHub.Application.Features.Auth.CheckEmail;
using ZonarHub.Application.Features.Auth.CheckPhone;
using ZonarHub.Application.Features.Auth.ForgotPassword;
using ZonarHub.Application.Features.Auth.Login;
using ZonarHub.Application.Features.Auth.Register;
using ZonarHub.Application.Features.Auth.ResetPassword;
using ZonarHub.Application.Features.Auth.SendVerificationCode;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ZonarHub.ApiService.Endpoints.Auth;

public static class AuthEndpointsExtensions
{
    private const string Tag = "Auth";
    private const string RoutePrefix = "/api/auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix).WithTags(Tag);

        group.MapPost("/send-verification-code", SendVerificationCodeAsync)
            .WithName("SendVerificationCode")
            .AllowAnonymous();

        group.MapPost("/check-code", CheckCodeAsync)
            .WithName("CheckVerificationCode")
            .AllowAnonymous();

        group.MapPost("/register", RegisterAsync)
            .WithName("Register")
            .AllowAnonymous();

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .AllowAnonymous();

        group.MapPost("/forgot-password", ForgotPasswordAsync)
            .WithName("ForgotPassword")
            .AllowAnonymous();

        group.MapPost("/reset-password", ResetPasswordAsync)
            .WithName("ResetPassword")
            .AllowAnonymous();

        group.MapGet("/check-email", CheckEmailAsync)
            .WithName("CheckEmail")
            .AllowAnonymous();

        group.MapGet("/check-phone", CheckPhoneAsync)
            .WithName("CheckPhone")
            .AllowAnonymous();

        return app;
    }

    private static async Task<IResult> SendVerificationCodeAsync(
        [FromBody] SendVerificationCodeRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SendVerificationCodeCommand(body.Email), cancellationToken);
        return result.Match(() => TypedResults.Ok());
    }

    private static async Task<IResult> CheckCodeAsync(
        [FromBody] CheckVerificationCodeRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CheckVerificationCodeQuery(body.Email, body.Code), cancellationToken);
        return result.Match(valid => (IResult)TypedResults.Ok(new { valid }));
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new RegisterCommand(
            body.FullName,
            body.Email,
            body.Password,
            body.Phone,
            body.BirthDate,
            body.VerificationCode);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(r => (IResult)TypedResults.Created("/api/auth/me", r));
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new LoginCommand(body.Email, body.Password), cancellationToken);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> ForgotPasswordAsync(
        [FromBody] ForgotPasswordRequest body,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var resetUrlBase = body.ResetUrlBase
            ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/reset-password";

        var result = await sender.Send(new ForgotPasswordCommand(body.Email, resetUrlBase), cancellationToken);
        return result.Match(() => TypedResults.Ok());
    }

    private static async Task<IResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ResetPasswordCommand(body.Token, body.NewPassword), cancellationToken);
        return result.Match(() => TypedResults.Ok());
    }

    private static async Task<IResult> CheckEmailAsync(
        [FromQuery] string email,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CheckEmailQuery(email), cancellationToken);
        return result.Match(exists => (IResult)TypedResults.Ok(new { exists }));
    }

    private static async Task<IResult> CheckPhoneAsync(
        [FromQuery] string phone,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CheckPhoneQuery(phone), cancellationToken);
        return result.Match(exists => (IResult)TypedResults.Ok(new { exists }));
    }
}

// ---- Request DTOs ----

public sealed record SendVerificationCodeRequest(string Email);

public sealed record CheckVerificationCodeRequest(string Email, string Code);

public sealed record RegisterRequest(
    string FullName,
    string Email,
    string Password,
    string? Phone,
    string? BirthDate,
    string VerificationCode);

public sealed record LoginRequest(string Email, string Password);

public sealed record ForgotPasswordRequest(string Email, string? ResetUrlBase);

public sealed record ResetPasswordRequest(string Token, string NewPassword);
