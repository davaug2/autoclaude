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
        Phase = new Phase
        {
            PhaseType = PhaseType.Analysis, Ordinal = 1,
            PromptTemplate = "Analise: {{objective}} em {{target_path}}"
        }
    };

    [Fact]
    public async Task HandleAsync_Success_ShouldUpdateContext()
    {
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult { ExitCode = 0, StandardOutput = "{\"result\":\"spec here\"}", DurationMs = 1000 });

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateContext());

        result.Success.Should().BeTrue();
        result.Output.Should().Be("spec here");
        _sessionRepo.Verify(r => r.UpdateContextAsync(It.IsAny<Guid>(), It.Is<string>(s => s.Contains("analysis_result"))), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Failure_ShouldReturnFailed()
    {
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult { ExitCode = 1, StandardError = "CLI error", DurationMs = 500 });

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateContext());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("CLI error");
    }

    [Fact]
    public async Task HandleAsync_ShouldCreateExecutionRecord()
    {
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult { ExitCode = 0, StandardOutput = "response", DurationMs = 1000 });

        var handler = CreateHandler();
        await handler.HandleAsync(CreateContext());

        _executionRepo.Verify(r => r.InsertAsync(It.IsAny<ExecutionRecord>()), Times.Once);
        _executionRepo.Verify(r => r.UpdateAsync(It.IsAny<ExecutionRecord>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ShouldUsePromptTemplate()
    {
        CliRequest? capturedRequest = null;
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CliRequest, CancellationToken>((r, _) => capturedRequest = r)
            .ReturnsAsync(new CliResult { ExitCode = 0, StandardOutput = "ok", DurationMs = 100 });

        var handler = CreateHandler();
        await handler.HandleAsync(CreateContext());

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Prompt.Should().Contain("Build a REST API");
        capturedRequest.Prompt.Should().Contain("/tmp/project");
    }

    [Fact]
    public async Task HandleAsync_ShouldCallExecutionStartedAndCompleted()
    {
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult { ExitCode = 0, StandardOutput = "{\"result\":\"ok\"}", DurationMs = 1000 });

        var handler = CreateHandler();
        await handler.HandleAsync(CreateContext());

        _notifier.Verify(n => n.OnExecutionStarted(It.IsAny<string>()), Times.Once);
        _notifier.Verify(n => n.OnExecutionCompleted(It.IsAny<ExecutionRecord>()), Times.Once);
    }
}
