using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.PhaseHandlers;
using AutoClaude.Core.Ports;
using FluentAssertions;
using Moq;

namespace AutoClaude.Core.Tests.PhaseHandlers;

public class ExecutionPhaseHandlerTests
{
    private readonly Mock<ICliExecutor> _cliExecutor = new();
    private readonly Mock<ISubtaskRepository> _subtaskRepo = new();
    private readonly Mock<IExecutionRecordRepository> _executionRepo = new();

    private ExecutionPhaseHandler CreateHandler() =>
        new(_cliExecutor.Object, _subtaskRepo.Object, _executionRepo.Object);

    [Fact]
    public async Task HandleAsync_Success_ShouldUpdateSubtaskAndCreateRecord()
    {
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult { ExitCode = 0, StandardOutput = "{\"result\":\"done\"}", DurationMs = 2000 });

        var subtask = new SubtaskItem { Title = "Sub 1", Prompt = "Do something" };
        var context = new PhaseContext
        {
            Session = new Session(),
            Phase = new Phase { PhaseType = PhaseType.Execution, Ordinal = 4 },
            CurrentTask = new TaskItem { Title = "Task 1" },
            CurrentSubtask = subtask
        };

        var handler = CreateHandler();
        var result = await handler.HandleAsync(context);

        result.Success.Should().BeTrue();
        _subtaskRepo.Verify(r => r.UpdateStatusAsync(subtask.Id, SubtaskItemStatus.Completed), Times.Once);
        _subtaskRepo.Verify(r => r.UpdateResultSummaryAsync(subtask.Id, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Failure_ShouldMarkSubtaskFailed()
    {
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult { ExitCode = 1, StandardError = "error", DurationMs = 500 });

        var subtask = new SubtaskItem { Title = "Sub 1", Prompt = "Do something" };
        var context = new PhaseContext
        {
            Session = new Session(),
            Phase = new Phase { PhaseType = PhaseType.Execution, Ordinal = 4 },
            CurrentTask = new TaskItem(),
            CurrentSubtask = subtask
        };

        var handler = CreateHandler();
        var result = await handler.HandleAsync(context);

        result.Success.Should().BeFalse();
        _subtaskRepo.Verify(r => r.UpdateStatusAsync(subtask.Id, SubtaskItemStatus.Failed), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_NoSubtask_ShouldFail()
    {
        var context = new PhaseContext
        {
            Session = new Session(),
            Phase = new Phase { PhaseType = PhaseType.Execution, Ordinal = 4 }
        };

        var handler = CreateHandler();
        var result = await handler.HandleAsync(context);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("requires a current subtask");
    }

    [Fact]
    public async Task HandleAsync_ShouldUseSubtaskPromptDirectly()
    {
        CliRequest? capturedRequest = null;
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CliRequest, CancellationToken>((r, _) => capturedRequest = r)
            .ReturnsAsync(new CliResult { ExitCode = 0, StandardOutput = "ok", DurationMs = 100 });

        var context = new PhaseContext
        {
            Session = new Session(),
            Phase = new Phase { PhaseType = PhaseType.Execution, Ordinal = 4 },
            CurrentTask = new TaskItem(),
            CurrentSubtask = new SubtaskItem { Prompt = "Execute this specific command" }
        };

        var handler = CreateHandler();
        await handler.HandleAsync(context);

        capturedRequest!.Prompt.Should().Be("Execute this specific command");
    }
}
