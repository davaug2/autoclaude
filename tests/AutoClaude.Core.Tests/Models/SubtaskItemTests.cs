using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using FluentAssertions;

namespace AutoClaude.Core.Tests.Models;

public class SubtaskItemTests
{
    [Fact]
    public void NewSubtaskItem_ShouldHaveDefaultValues()
    {
        var subtask = new SubtaskItem();

        subtask.Status.Should().Be(SubtaskItemStatus.Pending);
        subtask.Id.Should().NotBeEmpty();
        subtask.Prompt.Should().BeEmpty();
    }

    [Fact]
    public void MarkRunning_ShouldUpdateStatus()
    {
        var subtask = new SubtaskItem();

        subtask.MarkRunning();

        subtask.Status.Should().Be(SubtaskItemStatus.Running);
    }

    [Fact]
    public void MarkCompleted_ShouldUpdateStatusAndResult()
    {
        var subtask = new SubtaskItem();

        subtask.MarkCompleted("Resultado da execução");

        subtask.Status.Should().Be(SubtaskItemStatus.Completed);
        subtask.ResultSummary.Should().Be("Resultado da execução");
    }

    [Fact]
    public void MarkFailed_ShouldUpdateStatusAndResult()
    {
        var subtask = new SubtaskItem();

        subtask.MarkFailed("Erro inesperado");

        subtask.Status.Should().Be(SubtaskItemStatus.Failed);
        subtask.ResultSummary.Should().Be("Erro inesperado");
    }

    [Fact]
    public void MarkSkipped_ShouldUpdateStatus()
    {
        var subtask = new SubtaskItem();

        subtask.MarkSkipped();

        subtask.Status.Should().Be(SubtaskItemStatus.Skipped);
    }

    [Fact]
    public void SetValidation_ShouldUpdateValidationNote()
    {
        var subtask = new SubtaskItem();

        subtask.SetValidation("Validação aprovada");

        subtask.ValidationNote.Should().Be("Validação aprovada");
    }
}
