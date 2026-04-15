using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Infrastructure.Repositories;
using FluentAssertions;

namespace AutoClaude.Infrastructure.Tests.Repositories;

public class TaskRepositoryTests : RepositoryTestBase
{
    private async Task<Session> CreateSessionAsync()
    {
        var wmRepo = new WorkModelRepository(Factory);
        var wm = new WorkModel { Name = "TestFlow" };
        await wmRepo.InsertAsync(wm);

        var sessionRepo = new SessionRepository(Factory);
        var session = new Session { WorkModelId = wm.Id, Objective = "Test" };
        await sessionRepo.InsertAsync(session);
        return session;
    }

    [Fact]
    public async Task InsertAndGetById_ShouldRoundtrip()
    {
        var session = await CreateSessionAsync();
        var repo = new TaskRepository(Factory);
        var task = new TaskItem { SessionId = session.Id, Title = "Task 1", Description = "Do something", Ordinal = 1 };

        await repo.InsertAsync(task);
        var result = await repo.GetByIdAsync(task.Id);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Task 1");
        result.Description.Should().Be("Do something");
        result.Ordinal.Should().Be(1);
        result.Status.Should().Be(TaskItemStatus.Pending);
    }

    [Fact]
    public async Task GetBySessionId_ShouldReturnTasksInOrder()
    {
        var session = await CreateSessionAsync();
        var repo = new TaskRepository(Factory);
        await repo.InsertAsync(new TaskItem { SessionId = session.Id, Title = "Task 3", Ordinal = 3 });
        await repo.InsertAsync(new TaskItem { SessionId = session.Id, Title = "Task 1", Ordinal = 1 });
        await repo.InsertAsync(new TaskItem { SessionId = session.Id, Title = "Task 2", Ordinal = 2 });

        var result = await repo.GetBySessionIdAsync(session.Id);

        result.Should().HaveCount(3);
        result[0].Ordinal.Should().Be(1);
        result[1].Ordinal.Should().Be(2);
        result[2].Ordinal.Should().Be(3);
    }

    [Fact]
    public async Task UpdateStatus_ShouldChangeStatus()
    {
        var session = await CreateSessionAsync();
        var repo = new TaskRepository(Factory);
        var task = new TaskItem { SessionId = session.Id, Title = "Task 1", Ordinal = 1 };
        await repo.InsertAsync(task);

        await repo.UpdateStatusAsync(task.Id, TaskItemStatus.Completed);
        var result = await repo.GetByIdAsync(task.Id);

        result!.Status.Should().Be(TaskItemStatus.Completed);
    }
}
