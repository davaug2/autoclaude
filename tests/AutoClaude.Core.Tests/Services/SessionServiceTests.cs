using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using AutoClaude.Core.Services;
using FluentAssertions;
using Moq;

namespace AutoClaude.Core.Tests.Services;

public class SessionServiceTests
{
    private readonly Mock<ISessionRepository> _sessionRepo = new();
    private readonly Mock<IWorkModelRepository> _workModelRepo = new();
    private readonly Mock<ITaskRepository> _taskRepo = new();
    private readonly Mock<ISubtaskRepository> _subtaskRepo = new();
    private readonly Mock<IPhaseRepository> _phaseRepo = new();

    private SessionService CreateService()
    {
        var seeder = new WorkModelSeeder(_workModelRepo.Object, _phaseRepo.Object);

        var factory = new AutoClaude.Core.PhaseHandlers.PhaseHandlerFactory(
            Array.Empty<AutoClaude.Core.PhaseHandlers.IPhaseHandler>());

        var engine = new OrchestrationEngine(
            _phaseRepo.Object, _taskRepo.Object, _subtaskRepo.Object,
            _sessionRepo.Object, factory, new Mock<IOrchestrationNotifier>().Object);

        return new SessionService(
            _sessionRepo.Object, _workModelRepo.Object,
            _taskRepo.Object, _subtaskRepo.Object,
            seeder, engine);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateSessionLinkedToCascadeFlow()
    {
        var workModel = new WorkModel { Name = "CascadeFlow", IsBuiltin = true };
        _workModelRepo.Setup(r => r.GetByNameAsync("CascadeFlow")).ReturnsAsync(workModel);

        var service = CreateService();
        var session = await service.CreateAsync("Build REST API", targetPath: "/tmp/project");

        session.Should().NotBeNull();
        session.WorkModelId.Should().Be(workModel.Id);
        session.Objective.Should().Be("Build REST API");
        session.TargetPath.Should().Be("/tmp/project");
        _sessionRepo.Verify(r => r.InsertAsync(It.IsAny<Session>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_ShouldReturnAllSessions()
    {
        var sessions = new List<Session>
        {
            new() { Objective = "Session 1" },
            new() { Objective = "Session 2" }
        };
        _sessionRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(sessions);

        var service = CreateService();
        var result = await service.ListAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ResumeAsync_CompletedSession_ShouldThrow()
    {
        var session = new Session { Status = SessionStatus.Completed };
        _sessionRepo.Setup(r => r.GetByIdAsync(session.Id)).ReturnsAsync(session);

        var service = CreateService();
        var act = () => service.ResumeAsync(session.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already completed*");
    }

    [Fact]
    public async Task GetAsync_NonExistent_ShouldThrow()
    {
        _sessionRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Session?)null);

        var service = CreateService();
        var act = () => service.GetAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ListAsync_WhenEmpty_ShouldReturnEmptyList()
    {
        _sessionRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Session>());

        var service = CreateService();
        var result = await service.ListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnSessionReadyToRun()
    {
        var workModel = new WorkModel { Name = "CascadeFlow", IsBuiltin = true };
        _workModelRepo.Setup(r => r.GetByNameAsync("CascadeFlow")).ReturnsAsync(workModel);

        var service = CreateService();
        var session = await service.CreateAsync("Test objective");

        session.Status.Should().Be(SessionStatus.Created);
        session.WorkModelId.Should().Be(workModel.Id);
        session.Objective.Should().Be("Test objective");
    }
}
