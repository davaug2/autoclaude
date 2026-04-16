using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.PhaseHandlers;
using AutoClaude.Core.Ports;
using AutoClaude.Core.Services;
using FluentAssertions;
using Moq;

namespace AutoClaude.Core.Tests.Services;

public class OrchestrationEngineTests
{
    private readonly Mock<IPhaseRepository> _phaseRepo = new();
    private readonly Mock<ITaskRepository> _taskRepo = new();
    private readonly Mock<ISubtaskRepository> _subtaskRepo = new();
    private readonly Mock<ISessionRepository> _sessionRepo = new();
    private readonly Mock<IOrchestrationNotifier> _notifier = new();

    private OrchestrationEngine CreateEngine(params IPhaseHandler[] handlers)
    {
        var factory = new PhaseHandlerFactory(handlers);
        return new OrchestrationEngine(
            _phaseRepo.Object, _taskRepo.Object, _subtaskRepo.Object,
            _sessionRepo.Object, factory, _notifier.Object);
    }

    [Fact]
    public async Task RunAsync_ShouldIteratePhasesInOrder()
    {
        var phaseOrder = new List<PhaseType>();

        var analysisHandler = CreateMockHandler(PhaseType.Analysis, phaseOrder);
        var decompositionHandler = CreateMockHandler(PhaseType.Decomposition, phaseOrder);

        var phases = new List<Phase>
        {
            new() { Ordinal = 2, PhaseType = PhaseType.Decomposition, RepeatMode = RepeatMode.Once },
            new() { Ordinal = 1, PhaseType = PhaseType.Analysis, RepeatMode = RepeatMode.Once }
        };
        _phaseRepo.Setup(r => r.GetByWorkModelIdAsync(It.IsAny<Guid>())).ReturnsAsync(phases);
        _taskRepo.Setup(r => r.GetBySessionIdAsync(It.IsAny<Guid>())).ReturnsAsync(new List<TaskItem>());

        var session = new Session();
        var engine = CreateEngine(analysisHandler.Object, decompositionHandler.Object);
        await engine.RunAsync(session);

        phaseOrder.Should().ContainInOrder(PhaseType.Analysis, PhaseType.Decomposition);
    }

    [Fact]
    public async Task RunAsync_PerTask_ShouldCallHandlerPerTask()
    {
        var callCount = 0;
        var handler = new Mock<IPhaseHandler>();
        handler.Setup(h => h.HandledPhase).Returns(PhaseType.SubtaskCreation);
        handler.Setup(h => h.HandleAsync(It.IsAny<PhaseContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .ReturnsAsync(PhaseResult.Succeeded("ok"));

        var phases = new List<Phase> { new() { Ordinal = 1, PhaseType = PhaseType.SubtaskCreation, RepeatMode = RepeatMode.PerTask } };
        _phaseRepo.Setup(r => r.GetByWorkModelIdAsync(It.IsAny<Guid>())).ReturnsAsync(phases);

        var tasks = new List<TaskItem>
        {
            new() { Ordinal = 1, Title = "T1" },
            new() { Ordinal = 2, Title = "T2" },
            new() { Ordinal = 3, Title = "T3" }
        };
        _taskRepo.Setup(r => r.GetBySessionIdAsync(It.IsAny<Guid>())).ReturnsAsync(tasks);

        var engine = CreateEngine(handler.Object);
        await engine.RunAsync(new Session());

        callCount.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_PerSubtask_ShouldCallHandlerPerSubtask()
    {
        var callCount = 0;
        var handler = new Mock<IPhaseHandler>();
        handler.Setup(h => h.HandledPhase).Returns(PhaseType.Execution);
        handler.Setup(h => h.HandleAsync(It.IsAny<PhaseContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .ReturnsAsync(PhaseResult.Succeeded("ok"));

        var phases = new List<Phase> { new() { Ordinal = 1, PhaseType = PhaseType.Execution, RepeatMode = RepeatMode.PerSubtask } };
        _phaseRepo.Setup(r => r.GetByWorkModelIdAsync(It.IsAny<Guid>())).ReturnsAsync(phases);

        var task1 = new TaskItem { Ordinal = 1, Title = "T1" };
        _taskRepo.Setup(r => r.GetBySessionIdAsync(It.IsAny<Guid>())).ReturnsAsync(new List<TaskItem> { task1 });
        _subtaskRepo.Setup(r => r.GetByTaskIdAsync(task1.Id)).ReturnsAsync(new List<SubtaskItem>
        {
            new() { Ordinal = 1, Title = "S1", Prompt = "p1" },
            new() { Ordinal = 2, Title = "S2", Prompt = "p2" }
        });

        var engine = CreateEngine(handler.Object);
        await engine.RunAsync(new Session());

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_PhaseFailure_WithAbort_ShouldSetSessionFailed()
    {
        var handler = new Mock<IPhaseHandler>();
        handler.Setup(h => h.HandledPhase).Returns(PhaseType.Analysis);
        handler.Setup(h => h.HandleAsync(It.IsAny<PhaseContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PhaseResult.Failed("error"));

        var phases = new List<Phase> { new() { Ordinal = 1, PhaseType = PhaseType.Analysis, RepeatMode = RepeatMode.Once } };
        _phaseRepo.Setup(r => r.GetByWorkModelIdAsync(It.IsAny<Guid>())).ReturnsAsync(phases);
        _notifier.Setup(n => n.RequestUserDecision(It.IsAny<string>(), It.IsAny<UserDecision[]>()))
            .ReturnsAsync(UserDecision.Abort);

        var session = new Session();
        var engine = CreateEngine(handler.Object);
        await engine.RunAsync(session);

        session.Status.Should().Be(SessionStatus.Failed);
        _sessionRepo.Verify(r => r.UpdateStatusAsync(session.Id, SessionStatus.Failed), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ShouldNotifyPhaseEvents()
    {
        var handler = new Mock<IPhaseHandler>();
        handler.Setup(h => h.HandledPhase).Returns(PhaseType.Analysis);
        handler.Setup(h => h.HandleAsync(It.IsAny<PhaseContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PhaseResult.Succeeded("ok"));

        var phases = new List<Phase> { new() { Ordinal = 1, PhaseType = PhaseType.Analysis, RepeatMode = RepeatMode.Once } };
        _phaseRepo.Setup(r => r.GetByWorkModelIdAsync(It.IsAny<Guid>())).ReturnsAsync(phases);

        var engine = CreateEngine(handler.Object);
        await engine.RunAsync(new Session());

        _notifier.Verify(n => n.OnPhaseStarted(It.IsAny<Phase>(), It.IsAny<Session>()), Times.Once);
        _notifier.Verify(n => n.OnPhaseCompleted(It.IsAny<Phase>(), true, null), Times.Once);
    }

    [Fact]
    public async Task RunAsync_Resume_ShouldSkipCompletedPhases()
    {
        var callCount = 0;
        var handler = new Mock<IPhaseHandler>();
        handler.Setup(h => h.HandledPhase).Returns(PhaseType.Decomposition);
        handler.Setup(h => h.HandleAsync(It.IsAny<PhaseContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .ReturnsAsync(PhaseResult.Succeeded("ok"));

        var phases = new List<Phase>
        {
            new() { Ordinal = 1, PhaseType = PhaseType.Analysis, RepeatMode = RepeatMode.Once },
            new() { Ordinal = 2, PhaseType = PhaseType.Decomposition, RepeatMode = RepeatMode.Once }
        };
        _phaseRepo.Setup(r => r.GetByWorkModelIdAsync(It.IsAny<Guid>())).ReturnsAsync(phases);

        // Sessão já completou fase 1
        var session = new Session { CurrentPhaseOrdinal = 1 };

        // Precisa de um handler para Analysis tbm, mesmo q não seja chamado
        var analysisHandler = new Mock<IPhaseHandler>();
        analysisHandler.Setup(h => h.HandledPhase).Returns(PhaseType.Analysis);

        var engine = CreateEngine(analysisHandler.Object, handler.Object);
        await engine.RunAsync(session);

        callCount.Should().Be(1); // Só a decomposição
    }

    [Fact]
    public async Task RunAsync_PerSubtask_ShouldMarkTaskCompletedWhenAllSubtasksDone()
    {
        var handler = new Mock<IPhaseHandler>();
        handler.Setup(h => h.HandledPhase).Returns(PhaseType.Validation);
        handler.Setup(h => h.HandleAsync(It.IsAny<PhaseContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PhaseResult.Succeeded("ok"));

        var phases = new List<Phase> { new() { Ordinal = 1, PhaseType = PhaseType.Validation, RepeatMode = RepeatMode.PerSubtask } };
        _phaseRepo.Setup(r => r.GetByWorkModelIdAsync(It.IsAny<Guid>())).ReturnsAsync(phases);

        var task1 = new TaskItem { Ordinal = 1, Title = "T1" };
        _taskRepo.Setup(r => r.GetBySessionIdAsync(It.IsAny<Guid>())).ReturnsAsync(new List<TaskItem> { task1 });

        var sub1 = new SubtaskItem { Ordinal = 1, Title = "S1", Prompt = "p1", Status = SubtaskItemStatus.Completed };
        var sub2 = new SubtaskItem { Ordinal = 2, Title = "S2", Prompt = "p2" };

        var callCount = 0;
        _subtaskRepo.Setup(r => r.GetByTaskIdAsync(task1.Id))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount > 1)
                {
                    sub2.MarkCompleted("done");
                }
                return new List<SubtaskItem> { sub1, sub2 };
            });

        var engine = CreateEngine(handler.Object);
        await engine.RunAsync(new Session());

        _taskRepo.Verify(r => r.UpdateStatusAsync(task1.Id, TaskItemStatus.Completed), Times.AtLeastOnce);
    }

    private static Mock<IPhaseHandler> CreateMockHandler(PhaseType type, List<PhaseType> trackingList)
    {
        var handler = new Mock<IPhaseHandler>();
        handler.Setup(h => h.HandledPhase).Returns(type);
        handler.Setup(h => h.HandleAsync(It.IsAny<PhaseContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => trackingList.Add(type))
            .ReturnsAsync(PhaseResult.Succeeded("ok"));
        return handler;
    }
}
