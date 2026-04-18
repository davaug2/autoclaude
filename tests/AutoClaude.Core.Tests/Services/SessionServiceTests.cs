using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using AutoClaude.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AutoClaude.Core.Tests.Services;

public class SessionServiceTests
{
    private readonly Mock<ISessionRepository> _sessionRepo = new();
    private readonly Mock<IWorkModelRepository> _workModelRepo = new();
    private readonly Mock<ITaskRepository> _taskRepo = new();
    private readonly Mock<ISubtaskRepository> _subtaskRepo = new();
    private readonly Mock<IExecutionRecordRepository> _executionRepo = new();
    private readonly Mock<IPhaseRepository> _phaseRepo = new();

    private SessionService CreateService()
    {
        var seeder = new WorkModelSeeder(_workModelRepo.Object, _phaseRepo.Object);

        var factory = new AutoClaude.Core.PhaseHandlers.PhaseHandlerFactory(
            Array.Empty<AutoClaude.Core.PhaseHandlers.IPhaseHandler>());

        var engine = new OrchestrationEngine(
            _phaseRepo.Object, _taskRepo.Object, _subtaskRepo.Object,
            _sessionRepo.Object, new Mock<ICliExecutor>().Object, factory, new Mock<IOrchestrationNotifier>().Object,
            NullLogger<OrchestrationEngine>.Instance);

        return new SessionService(
            _sessionRepo.Object, _workModelRepo.Object,
            _taskRepo.Object, _subtaskRepo.Object,
            _executionRepo.Object, _phaseRepo.Object, seeder, engine);
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

    [Fact]
    public async Task CreateAsync_WithWorkModelId_ShouldUseSpecifiedModel()
    {
        var customModel = new WorkModel { Name = "CustomFlow" };
        _workModelRepo.Setup(r => r.GetByIdAsync(customModel.Id)).ReturnsAsync(customModel);
        _workModelRepo.Setup(r => r.GetByNameAsync("CascadeFlow")).ReturnsAsync(new WorkModel { Name = "CascadeFlow" });

        var service = CreateService();
        var session = await service.CreateAsync("Test", workModelId: customModel.Id);

        session.WorkModelId.Should().Be(customModel.Id);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidWorkModelId_ShouldThrow()
    {
        _workModelRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((WorkModel?)null);
        _workModelRepo.Setup(r => r.GetByNameAsync("CascadeFlow")).ReturnsAsync(new WorkModel { Name = "CascadeFlow" });

        var service = CreateService();
        var act = () => service.CreateAsync("Test", workModelId: Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ListWorkModelsAsync_ShouldReturnAllModels()
    {
        var models = new List<WorkModel>
        {
            new() { Name = "CascadeFlow", IsBuiltin = true },
            new() { Name = "CustomFlow" }
        };
        _workModelRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(models);

        var service = CreateService();
        var result = await service.ListWorkModelsAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateWorkModelAsync_ShouldInsertAndReturnModel()
    {
        var service = CreateService();
        var model = await service.CreateWorkModelAsync("MyFlow", "Custom pipeline");

        model.Name.Should().Be("MyFlow");
        model.Description.Should().Be("Custom pipeline");
        model.IsBuiltin.Should().BeFalse();
        _workModelRepo.Verify(r => r.InsertAsync(It.Is<WorkModel>(m => m.Name == "MyFlow")), Times.Once);
    }

    [Fact]
    public async Task AddPhaseToWorkModelAsync_ShouldInsertPhase()
    {
        var service = CreateService();
        var modelId = Guid.NewGuid();

        var phase = await service.AddPhaseToWorkModelAsync(
            modelId, "Análise", PhaseType.Analysis, 1, RepeatMode.Once, "Analisa o código");

        phase.WorkModelId.Should().Be(modelId);
        phase.Name.Should().Be("Análise");
        phase.PhaseType.Should().Be(PhaseType.Analysis);
        phase.Ordinal.Should().Be(1);
        _phaseRepo.Verify(r => r.InsertAsync(It.IsAny<Phase>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteSessionAndRelatedData()
    {
        var session = new Session { Objective = "Test" };
        _sessionRepo.Setup(r => r.GetByIdAsync(session.Id)).ReturnsAsync(session);
        _taskRepo.Setup(r => r.GetBySessionIdAsync(session.Id)).ReturnsAsync(new List<TaskItem>
        {
            new() { Id = Guid.NewGuid(), SessionId = session.Id, Title = "T1", Ordinal = 1 }
        });
        _subtaskRepo.Setup(r => r.GetBySessionIdAsync(session.Id)).ReturnsAsync(new List<SubtaskItem>());

        var service = CreateService();
        await service.DeleteAsync(session.Id);

        _sessionRepo.Verify(r => r.DeleteAsync(session.Id), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_ShouldThrow()
    {
        _sessionRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Session?)null);

        var service = CreateService();
        var act = () => service.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
