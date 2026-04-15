using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using FluentAssertions;

namespace AutoClaude.Core.Tests.Models;

public class TaskItemTests
{
    [Fact]
    public void NewTaskItem_ShouldHaveDefaultStatus()
    {
        var task = new TaskItem();

        task.Status.Should().Be(TaskItemStatus.Pending);
        task.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void MarkInProgress_ShouldUpdateStatus()
    {
        var task = new TaskItem();

        task.MarkInProgress();

        task.Status.Should().Be(TaskItemStatus.InProgress);
    }

    [Fact]
    public void MarkCompleted_ShouldUpdateStatusAndResult()
    {
        var task = new TaskItem();

        task.MarkCompleted("Tarefa concluída com sucesso");

        task.Status.Should().Be(TaskItemStatus.Completed);
        task.ResultSummary.Should().Be("Tarefa concluída com sucesso");
    }

    [Fact]
    public void MarkFailed_ShouldUpdateStatusAndResult()
    {
        var task = new TaskItem();

        task.MarkFailed("Erro na execução");

        task.Status.Should().Be(TaskItemStatus.Failed);
        task.ResultSummary.Should().Be("Erro na execução");
    }
}
