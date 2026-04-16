using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.PhaseHandlers;
using AutoClaude.Core.Ports;
using FluentAssertions;
using Moq;

namespace AutoClaude.Core.Tests.PhaseHandlers;

public class DecompositionPhaseHandlerTests
{
    private readonly Mock<ICliExecutor> _cliExecutor = new();
    private readonly Mock<ISessionRepository> _sessionRepo = new();
    private readonly Mock<ITaskRepository> _taskRepo = new();
    private readonly Mock<IExecutionRecordRepository> _executionRepo = new();
    private readonly Mock<IOrchestrationNotifier> _notifier = new();

    private DecompositionPhaseHandler CreateHandler() =>
        new(_cliExecutor.Object, _sessionRepo.Object, _taskRepo.Object, _executionRepo.Object, _notifier.Object);

    [Fact]
    public async Task HandleAsync_ShouldShowTasksAndConfirmWithUser()
    {
        var jsonResponse = "{\"result\":\"[{\\\"title\\\":\\\"Task 1\\\",\\\"description\\\":\\\"Desc 1\\\"},{\\\"title\\\":\\\"Task 2\\\",\\\"description\\\":\\\"Desc 2\\\"}]\"}";
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult { ExitCode = 0, StandardOutput = jsonResponse, DurationMs = 1000 });

        _notifier.Setup(n => n.ConfirmWithUser(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var context = new PhaseContext
        {
            Session = new Session { Objective = "Build API", ContextJson = "{\"analysis_result\":\"spec\"}" },
            Phase = new Phase { PhaseType = PhaseType.Decomposition, Ordinal = 2 }
        };

        var handler = CreateHandler();
        await handler.HandleAsync(context);

        _notifier.Verify(n => n.ConfirmWithUser(
            It.Is<string>(s => s.Contains("tarefa") || s.Contains("Tarefa") || s.Contains("task")),
            It.IsAny<string>()), Times.Once);
        _taskRepo.Verify(r => r.InsertAsync(It.IsAny<TaskItem>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_WhenUserRejectsTasks_ShouldReturnFailed()
    {
        var jsonResponse = "{\"result\":\"[{\\\"title\\\":\\\"Task 1\\\",\\\"description\\\":\\\"Desc 1\\\"}]\"}";
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult { ExitCode = 0, StandardOutput = jsonResponse, DurationMs = 1000 });

        _notifier.Setup(n => n.ConfirmWithUser(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

        var context = new PhaseContext
        {
            Session = new Session { ContextJson = "{}" },
            Phase = new Phase { PhaseType = PhaseType.Decomposition, Ordinal = 2 }
        };

        var handler = CreateHandler();
        var result = await handler.HandleAsync(context);

        result.Success.Should().BeFalse();
        _taskRepo.Verify(r => r.InsertAsync(It.IsAny<TaskItem>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_CliFailure_ShouldNotCreateTasks()
    {
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult { ExitCode = 1, StandardError = "Error", DurationMs = 100 });

        var context = new PhaseContext
        {
            Session = new Session { ContextJson = "{}" },
            Phase = new Phase { PhaseType = PhaseType.Decomposition, Ordinal = 2 }
        };

        var handler = CreateHandler();
        var result = await handler.HandleAsync(context);

        result.Success.Should().BeFalse();
        _taskRepo.Verify(r => r.InsertAsync(It.IsAny<TaskItem>()), Times.Never);
    }
}
