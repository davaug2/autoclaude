using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.PhaseHandlers;
using AutoClaude.Core.Ports;
using FluentAssertions;
using Moq;

namespace AutoClaude.Core.Tests.PhaseHandlers;

public class AnalysisPhaseHandlerTests
{
    private readonly Mock<ICliExecutor> _cliExecutor = new();
    private readonly Mock<ISessionRepository> _sessionRepo = new();
    private readonly Mock<IExecutionRecordRepository> _executionRepo = new();
    private readonly Mock<IOrchestrationNotifier> _notifier = new();

    private AnalysisPhaseHandler CreateHandler() =>
        new(_cliExecutor.Object, _sessionRepo.Object, _executionRepo.Object, _notifier.Object);

    private PhaseContext CreateContext() => new()
    {
        Session = new Session { Objective = "Build a REST API", TargetPath = "/tmp/project", ContextJson = "{}" },
        Phase = new Phase { PhaseType = PhaseType.Analysis, Ordinal = 1 }
    };

    [Fact]
    public async Task HandleAsync_ShouldAskClaudeForQuestions()
    {
        var callIndex = 0;
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callIndex++;
                if (callIndex == 1)
                    return new CliResult { ExitCode = 0, StandardOutput = "{\"result\":\"{\\\"questions\\\":[\\\"Qual framework?\\\",\\\"Qual banco?\\\"]}\"}",  DurationMs = 100 };
                return new CliResult { ExitCode = 0, StandardOutput = "{\"result\":\"Spec elaborada\"}", DurationMs = 100 };
            });

        _notifier.Setup(n => n.AskUserTextInput(It.IsAny<string>())).ReturnsAsync("resposta");
        _notifier.Setup(n => n.ConfirmWithUser(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((AutoClaude.Core.Domain.Enums.ConfirmationResult.Confirm, (string?)null));

        var handler = CreateHandler();
        await handler.HandleAsync(CreateContext());

        _notifier.Verify(n => n.AskUserTextInput(It.Is<string>(s => s.Contains("Qual framework?"))), Times.Once);
        _notifier.Verify(n => n.AskUserTextInput(It.Is<string>(s => s.Contains("Qual banco?"))), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ShouldElaborateObjectiveAndConfirm()
    {
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult { ExitCode = 0, StandardOutput = "{\"result\":\"{\\\"questions\\\":[]}\"}", DurationMs = 100 });

        _notifier.Setup(n => n.ConfirmWithUser(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((AutoClaude.Core.Domain.Enums.ConfirmationResult.Confirm, (string?)null));

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateContext());

        _notifier.Verify(n => n.ConfirmWithUser(
            It.Is<string>(s => s.Contains("Objetivo")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenUserRejectsObjective_ShouldReturnFailed()
    {
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult { ExitCode = 0, StandardOutput = "{\"result\":\"{\\\"questions\\\":[]}\"}", DurationMs = 100 });

        _notifier.Setup(n => n.ConfirmWithUser(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((AutoClaude.Core.Domain.Enums.ConfirmationResult.Reject, (string?)null));

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateContext());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ShouldCallExecutionStartedAndCompleted()
    {
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult { ExitCode = 0, StandardOutput = "{\"result\":\"{\\\"questions\\\":[]}\"}", DurationMs = 1000 });

        _notifier.Setup(n => n.ConfirmWithUser(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((AutoClaude.Core.Domain.Enums.ConfirmationResult.Confirm, (string?)null));

        var handler = CreateHandler();
        await handler.HandleAsync(CreateContext());

        _notifier.Verify(n => n.OnExecutionStarted(It.IsAny<string>()), Times.AtLeastOnce);
        _notifier.Verify(n => n.OnExecutionCompleted(It.IsAny<ExecutionRecord>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task HandleAsync_CliFailure_ShouldReturnFailed()
    {
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult { ExitCode = 1, StandardError = "CLI error", DurationMs = 500 });

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateContext());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("CLI error");
    }
}
