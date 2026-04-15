using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Infrastructure.Repositories;
using FluentAssertions;

namespace AutoClaude.Infrastructure.Tests.Repositories;

public class SubtaskRepositoryTests : RepositoryTestBase
{
    private async Task<(Session session, TaskItem task)> CreateTaskAsync()
    {
        var wmRepo = new WorkModelRepository(Factory);
        var wm = new WorkModel { Name = "TestFlow" };
        await wmRepo.InsertAsync(wm);

        var sessionRepo = new SessionRepository(Factory);
        var session = new Session { WorkModelId = wm.Id, Objective = "Test" };
        await sessionRepo.InsertAsync(session);

        var taskRepo = new TaskRepository(Factory);
        var task = new TaskItem { SessionId = session.Id, Title = "Task 1", Ordinal = 1 };
        await taskRepo.InsertAsync(task);

        return (session, task);
    }

    [Fact]
    public async Task InsertAndGetById_ShouldRoundtrip()
    {
        var (session, task) = await CreateTaskAsync();
        var repo = new SubtaskRepository(Factory);
        var subtask = new SubtaskItem
        {
            TaskId = task.Id, SessionId = session.Id,
            Title = "Subtask 1", Prompt = "Do this thing", Ordinal = 1
        };

        await repo.InsertAsync(subtask);
        var result = await repo.GetByIdAsync(subtask.Id);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Subtask 1");
        result.Prompt.Should().Be("Do this thing");
        result.SessionId.Should().Be(session.Id);
        result.Status.Should().Be(SubtaskItemStatus.Pending);
    }

    [Fact]
    public async Task GetByTaskId_ShouldReturnSubtasksInOrder()
    {
        var (session, task) = await CreateTaskAsync();
        var repo = new SubtaskRepository(Factory);
        await repo.InsertAsync(new SubtaskItem { TaskId = task.Id, SessionId = session.Id, Title = "Sub 2", Prompt = "p2", Ordinal = 2 });
        await repo.InsertAsync(new SubtaskItem { TaskId = task.Id, SessionId = session.Id, Title = "Sub 1", Prompt = "p1", Ordinal = 1 });

        var result = await repo.GetByTaskIdAsync(task.Id);

        result.Should().HaveCount(2);
        result[0].Ordinal.Should().Be(1);
        result[1].Ordinal.Should().Be(2);
    }

    [Fact]
    public async Task UpdateValidationNote_ShouldChange()
    {
        var (session, task) = await CreateTaskAsync();
        var repo = new SubtaskRepository(Factory);
        var subtask = new SubtaskItem { TaskId = task.Id, SessionId = session.Id, Title = "Sub", Prompt = "p", Ordinal = 1 };
        await repo.InsertAsync(subtask);

        await repo.UpdateValidationNoteAsync(subtask.Id, "Looks good");
        var result = await repo.GetByIdAsync(subtask.Id);

        result!.ValidationNote.Should().Be("Looks good");
    }
}
