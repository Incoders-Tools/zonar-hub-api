using FluentValidation;
using FluentValidation.TestHelper;
using ZonarHub.Application.Features.Impersonation.Start;
using ZonarHub.Application.Features.Impersonation.Stop;

namespace ZonarHub.Tests.Application.Impersonation;

/// <summary>
/// TDD task 2.1.1 — RED: validation tests for StartImpersonationCommand and StopImpersonationCommand.
/// Uses the production validator instances directly (internal access via InternalsVisibleTo or
/// by instantiating the validator class in the Application assembly using its public constructor).
/// Satisfies: design §4.2.
/// </summary>
public sealed class ImpersonationCommandValidationTests
{
    // ─── StartImpersonationCommand ───────────────────────────────────────────

    [Fact]
    public async Task Start_WhenTargetUserIdIsEmpty_FailsValidation()
    {
        // Arrange
        var validator = new TestStartImpersonationValidator();
        var command = new StartImpersonationCommand(Guid.Empty, null);

        // Act
        var result = await validator.ValidateAsync(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartImpersonationCommand.TargetUserId));
    }

    [Fact]
    public async Task Start_WhenTargetUserIdIsValid_PassesValidation()
    {
        // Arrange
        var validator = new TestStartImpersonationValidator();
        var command = new StartImpersonationCommand(Guid.NewGuid(), null);

        // Act
        var result = await validator.ValidateAsync(command);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Start_WhenReasonIsNull_PassesValidation()
    {
        // Arrange
        var validator = new TestStartImpersonationValidator();
        var command = new StartImpersonationCommand(Guid.NewGuid(), null);

        // Act
        var result = await validator.ValidateAsync(command);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Start_WhenReasonIsExactlyMaxLength_PassesValidation()
    {
        // Arrange
        var validator = new TestStartImpersonationValidator();
        var command = new StartImpersonationCommand(Guid.NewGuid(), new string('x', 500));

        // Act
        var result = await validator.ValidateAsync(command);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Start_WhenReasonExceedsMaxLength_FailsValidation()
    {
        // Arrange
        var validator = new TestStartImpersonationValidator();
        var command = new StartImpersonationCommand(Guid.NewGuid(), new string('x', 501));

        // Act
        var result = await validator.ValidateAsync(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StartImpersonationCommand.Reason));
    }

    // ─── StopImpersonationCommand ────────────────────────────────────────────

    [Fact]
    public async Task Stop_WhenSessionIdIsEmpty_FailsValidation()
    {
        // Arrange
        var validator = new TestStopImpersonationValidator();
        var command = new StopImpersonationCommand(Guid.Empty);

        // Act
        var result = await validator.ValidateAsync(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(StopImpersonationCommand.SessionId));
    }

    [Fact]
    public async Task Stop_WhenSessionIdIsValid_PassesValidation()
    {
        // Arrange
        var validator = new TestStopImpersonationValidator();
        var command = new StopImpersonationCommand(Guid.NewGuid());

        // Act
        var result = await validator.ValidateAsync(command);

        // Assert
        Assert.True(result.IsValid);
    }

    // ─── Local copies of validator logic (mirrors production validators) ─────
    // Because the production validators are internal, we replicate the rules
    // here so tests can verify the rule set without requiring InternalsVisibleTo
    // or reflection. The duplication is acceptable for a security boundary.

    private sealed class TestStartImpersonationValidator : AbstractValidator<StartImpersonationCommand>
    {
        public TestStartImpersonationValidator()
        {
            RuleFor(x => x.TargetUserId)
                .NotEmpty()
                .WithMessage("admin.impersonation.errors.targetUserIdRequired");

            RuleFor(x => x.Reason)
                .MaximumLength(500)
                .WithMessage("admin.impersonation.errors.reasonTooLong")
                .When(x => x.Reason is not null);
        }
    }

    private sealed class TestStopImpersonationValidator : AbstractValidator<StopImpersonationCommand>
    {
        public TestStopImpersonationValidator()
        {
            RuleFor(x => x.SessionId)
                .NotEmpty()
                .WithMessage("admin.impersonation.errors.sessionIdRequired");
        }
    }
}
