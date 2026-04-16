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
    public async Task HandleAsync_ShouldCreateTasksFromJsonResponse()
    {
        var jsonResponse = "{\"result\":\"[{\\\"title\\\":\\\"Task 1\\\",\\\"description\\\":\\\"Desc 1\\\"},{\\\"title\\\":\\\"Task 2\\\",\\\"description\\\":\\\"Desc 2\\\"},{\\\"title\\\":\\\"Task 3\\\",\\\"description\\\":\\\"Desc 3\\\"}]\"}";
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult { ExitCode = 0, StandardOutput = jsonResponse, DurationMs = 1000 });

        var context = new PhaseContext
        {
            Session = new Session { Objective = "Build API", ContextJson = "{\"analysis_result\":\"spec\"}" },
            Phase = new Phase { PhaseType = PhaseType.Decomposition, Ordinal = 2 }
        };

        var handler = CreateHandler();
        var result = await handler.HandleAsync(context);

        result.Success.Should().BeTrue();
        _taskRepo.Verify(r => r.InsertAsync(It.IsAny<TaskItem>()), Times.Exactly(3));
    }

    [Fact]
    public async Task HandleAsync_Failure_ShouldNotCreateTasks()
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
