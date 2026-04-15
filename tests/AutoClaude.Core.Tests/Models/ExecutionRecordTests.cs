using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using FluentAssertions;

namespace AutoClaude.Core.Tests.Models;

public class ExecutionRecordTests
{
    [Fact]
    public void NewExecutionRecord_ShouldHaveDefaultValues()
    {
        var record = new ExecutionRecord();

        record.Id.Should().NotBeEmpty();
        record.CliType.Should().Be("claude");
        record.Outcome.Should().Be(ExecutionOutcome.Pending);
    }

    [Fact]
    public void MarkStarted_ShouldSetStartedAtAndOutcome()
    {
        var record = new ExecutionRecord();

        record.MarkStarted();

        record.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        record.Outcome.Should().Be(ExecutionOutcome.Pending);
    }

    [Fact]
    public void MarkSuccess_ShouldSetAllFields()
    {
        var record = new ExecutionRecord();
        record.MarkStarted();

        record.MarkSuccess("response text", "{\"result\":\"ok\"}", 0, 1500);

        record.Outcome.Should().Be(ExecutionOutcome.Success);
        record.ResponseText.Should().Be("response text");
        record.ResponseJson.Should().Be("{\"result\":\"ok\"}");
        record.ExitCode.Should().Be(0);
        record.DurationMs.Should().Be(1500);
        record.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkFailure_ShouldSetErrorFields()
    {
        var record = new ExecutionRecord();
        record.MarkStarted();

        record.MarkFailure("Process timed out", -1, 60000);

        record.Outcome.Should().Be(ExecutionOutcome.Failure);
        record.ErrorMessage.Should().Be("Process timed out");
        record.ExitCode.Should().Be(-1);
        record.DurationMs.Should().Be(60000);
        record.CompletedAt.Should().NotBeNull();
    }
}
