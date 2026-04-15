using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.PhaseHandlers;
using AutoClaude.Core.Ports;
using FluentAssertions;
using Moq;

namespace AutoClaude.Core.Tests.PhaseHandlers;

public class ValidationPhaseHandlerTests
{
    private readonly Mock<ICliExecutor> _cliExecutor = new();
    private readonly Mock<ISubtaskRepository> _subtaskRepo = new();
    private readonly Mock<IExecutionRecordRepository> _executionRepo = new();

    private ValidationPhaseHandler CreateHandler() =>
        new(_cliExecutor.Object, _subtaskRepo.Object, _executionRepo.Object);

    [Fact]
    public async Task HandleAsync_Valid_ShouldMarkCompleted()
    {
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult
            {
                ExitCode = 0,
                StandardOutput = "{\"result\":\"{\\\"valid\\\": true, \\\"note\\\": \\\"All good\\\"}\"}",
                DurationMs = 1000
            });

        var subtask = new SubtaskItem { Title = "Sub 1", Prompt = "Do X", ResultSummary = "Done" };
        var context = new PhaseContext
        {
            Session = new Session(),
            Phase = new Phase { PhaseType = PhaseType.Validation, Ordinal = 5 },
            CurrentTask = new TaskItem(),
            CurrentSubtask = subtask
        };

        var handler = CreateHandler();
        var result = await handler.HandleAsync(context);

        result.Success.Should().BeTrue();
        _subtaskRepo.Verify(r => r.UpdateStatusAsync(subtask.Id, SubtaskItemStatus.Completed), Times.Once);
        _subtaskRepo.Verify(r => r.UpdateValidationNoteAsync(subtask.Id, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Invalid_ShouldMarkFailed()
    {
        _cliExecutor.Setup(c => c.ExecuteAsync(It.IsAny<CliRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliResult
            {
                ExitCode = 0,
                StandardOutput = "{\"result\":\"{\\\"valid\\\": false, \\\"note\\\": \\\"Missing tests\\\"}\"}",
                DurationMs = 1000
            });

        var subtask = new SubtaskItem { Title = "Sub 1", Prompt = "Do X", ResultSummary = "Done" };
        var context = new PhaseContext
        {
            Session = new Session(),
            Phase = new Phase { PhaseType = PhaseType.Validation, Ordinal = 5 },
            CurrentTask = new TaskItem(),
            CurrentSubtask = subtask
        };

        var handler = CreateHandler();
        var result = await handler.HandleAsync(context);

        result.Success.Should().BeFalse();
        _subtaskRepo.Verify(r => r.UpdateStatusAsync(subtask.Id, SubtaskItemStatus.Failed), Times.Once);
    }
}
