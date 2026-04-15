using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Infrastructure.Repositories;
using FluentAssertions;

namespace AutoClaude.Infrastructure.Tests.Repositories;

public class SessionRepositoryTests : RepositoryTestBase
{
    private async Task<WorkModel> CreateWorkModelAsync()
    {
        var wmRepo = new WorkModelRepository(Factory);
        var model = new WorkModel { Name = "TestFlow" };
        await wmRepo.InsertAsync(model);
        return model;
    }

    [Fact]
    public async Task InsertAndGetById_ShouldRoundtrip()
    {
        var wm = await CreateWorkModelAsync();
        var repo = new SessionRepository(Factory);
        var session = new Session
        {
            WorkModelId = wm.Id, Name = "Test Session",
            Objective = "Build something", TargetPath = "/tmp/test"
        };

        await repo.InsertAsync(session);
        var result = await repo.GetByIdAsync(session.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Test Session");
        result.Objective.Should().Be("Build something");
        result.TargetPath.Should().Be("/tmp/test");
        result.Status.Should().Be(SessionStatus.Created);
        result.ContextJson.Should().Be("{}");
    }

    [Fact]
    public async Task UpdateStatus_ShouldChangeStatus()
    {
        var wm = await CreateWorkModelAsync();
        var repo = new SessionRepository(Factory);
        var session = new Session { WorkModelId = wm.Id, Objective = "Test" };
        await repo.InsertAsync(session);

        await repo.UpdateStatusAsync(session.Id, SessionStatus.Running);
        var result = await repo.GetByIdAsync(session.Id);

        result!.Status.Should().Be(SessionStatus.Running);
    }

    [Fact]
    public async Task UpdateContext_ShouldChangeContextJson()
    {
        var wm = await CreateWorkModelAsync();
        var repo = new SessionRepository(Factory);
        var session = new Session { WorkModelId = wm.Id, Objective = "Test" };
        await repo.InsertAsync(session);

        await repo.UpdateContextAsync(session.Id, "{\"analysis\":\"done\"}");
        var result = await repo.GetByIdAsync(session.Id);

        result!.ContextJson.Should().Be("{\"analysis\":\"done\"}");
    }

    [Fact]
    public async Task UpdateCurrentPhaseOrdinal_ShouldChangeOrdinal()
    {
        var wm = await CreateWorkModelAsync();
        var repo = new SessionRepository(Factory);
        var session = new Session { WorkModelId = wm.Id, Objective = "Test" };
        await repo.InsertAsync(session);

        await repo.UpdateCurrentPhaseOrdinalAsync(session.Id, 3);
        var result = await repo.GetByIdAsync(session.Id);

        result!.CurrentPhaseOrdinal.Should().Be(3);
    }

    [Fact]
    public async Task GetAll_ShouldReturnAllSessions()
    {
        var wm = await CreateWorkModelAsync();
        var repo = new SessionRepository(Factory);
        await repo.InsertAsync(new Session { WorkModelId = wm.Id, Objective = "Session 1" });
        await repo.InsertAsync(new Session { WorkModelId = wm.Id, Objective = "Session 2" });

        var result = await repo.GetAllAsync();

        result.Should().HaveCount(2);
    }
}
