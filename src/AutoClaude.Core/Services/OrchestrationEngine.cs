using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.PhaseHandlers;
using AutoClaude.Core.Ports;

namespace AutoClaude.Core.Services;

public class OrchestrationEngine
{
    private readonly IPhaseRepository _phaseRepo;
    private readonly ITaskRepository _taskRepo;
    private readonly ISubtaskRepository _subtaskRepo;
    private readonly ISessionRepository _sessionRepo;
    private readonly PhaseHandlerFactory _handlerFactory;
    private readonly IOrchestrationNotifier _notifier;

    public OrchestrationEngine(
        IPhaseRepository phaseRepo,
        ITaskRepository taskRepo,
        ISubtaskRepository subtaskRepo,
        ISessionRepository sessionRepo,
        PhaseHandlerFactory handlerFactory,
        IOrchestrationNotifier notifier)
    {
        _phaseRepo = phaseRepo;
        _taskRepo = taskRepo;
        _subtaskRepo = subtaskRepo;
        _sessionRepo = sessionRepo;
        _handlerFactory = handlerFactory;
        _notifier = notifier;
    }

    public async Task RunAsync(Session session, CancellationToken ct = default)
    {
        session.UpdateStatus(SessionStatus.Running);
        await _sessionRepo.UpdateStatusAsync(session.Id, SessionStatus.Running);

        var phases = await _phaseRepo.GetByWorkModelIdAsync(session.WorkModelId);
        var orderedPhases = phases.OrderBy(p => p.Ordinal).ToList();

        foreach (var phase in orderedPhases)
        {
            if (phase.Ordinal <= session.CurrentPhaseOrdinal)
                continue;

            var handler = _handlerFactory.GetHandler(phase.PhaseType);
            await _notifier.OnPhaseStarted(phase, session);

            bool phaseSuccess;

            switch (phase.RepeatMode)
            {
                case RepeatMode.Once:
                    phaseSuccess = await ExecuteWithInterruptAsync(handler, session, phase, null, null, ct);
                    break;
                case RepeatMode.PerTask:
                    phaseSuccess = await ExecutePerTaskAsync(handler, session, phase, ct);
                    break;
                case RepeatMode.PerSubtask:
                    phaseSuccess = await ExecutePerSubtaskAsync(handler, session, phase, ct);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown repeat mode: {phase.RepeatMode}");
            }

            await _notifier.OnPhaseCompleted(phase, phaseSuccess);

            if (!phaseSuccess)
            {
                var decision = await _notifier.RequestUserDecision(
                    $"Phase '{phase.Name}' failed. What would you like to do?",
                    new[] { UserDecision.Abort, UserDecision.Continue, UserDecision.Retry });

                switch (decision)
                {
                    case UserDecision.Abort:
                        session.UpdateStatus(SessionStatus.Failed);
                        await _sessionRepo.UpdateStatusAsync(session.Id, SessionStatus.Failed);
                        return;
                    case UserDecision.Pause:
                        session.UpdateStatus(SessionStatus.Paused);
                        await _sessionRepo.UpdateStatusAsync(session.Id, SessionStatus.Paused);
                        return;
                    case UserDecision.Continue:
                        break;
                    case UserDecision.Retry:
                        continue;
                }
            }

            session.AdvancePhase(phase.Ordinal);
            await _sessionRepo.UpdateCurrentPhaseOrdinalAsync(session.Id, phase.Ordinal);
        }

        session.UpdateStatus(SessionStatus.Completed);
        await _sessionRepo.UpdateStatusAsync(session.Id, SessionStatus.Completed);
    }

    private async Task<bool> ExecuteWithInterruptAsync(
        IPhaseHandler handler, Session session, Phase phase,
        TaskItem? task, SubtaskItem? subtask, CancellationToken ct)
    {
        var context = new PhaseContext
        {
            Session = session, Phase = phase,
            CurrentTask = task, CurrentSubtask = subtask
        };

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            _notifier.ResetInterruptToken();
            var interruptToken = _notifier.CreateInterruptToken();

            try
            {
                var result = await handler.HandleAsync(context, interruptToken);
                return result.Success;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var userInput = await _notifier.OnUserInterrupt();
                if (userInput == null)
                {
                    session.UpdateStatus(SessionStatus.Paused);
                    await _sessionRepo.UpdateStatusAsync(session.Id, SessionStatus.Paused);
                    return false;
                }

                context = new PhaseContext
                {
                    Session = session, Phase = phase,
                    CurrentTask = task, CurrentSubtask = subtask,
                    UserInstruction = userInput
                };
            }
        }
    }

    private async Task<bool> ExecutePerTaskAsync(IPhaseHandler handler, Session session, Phase phase, CancellationToken ct)
    {
        var tasks = await _taskRepo.GetBySessionIdAsync(session.Id);
        var orderedTasks = tasks.OrderBy(t => t.Ordinal).ToList();

        foreach (var task in orderedTasks)
        {
            if (task.Status == TaskItemStatus.Completed) continue;

            await _notifier.OnTaskStarted(task);
            await _taskRepo.UpdateStatusAsync(task.Id, TaskItemStatus.InProgress);

            var success = await ExecuteWithInterruptAsync(handler, session, phase, task, null, ct);

            if (!success)
            {
                await _taskRepo.UpdateStatusAsync(task.Id, TaskItemStatus.Failed);

                var decision = await _notifier.RequestUserDecision(
                    $"Task '{task.Title}' failed. What would you like to do?",
                    new[] { UserDecision.Abort, UserDecision.Continue, UserDecision.Retry });

                switch (decision)
                {
                    case UserDecision.Abort:
                        return false;
                    case UserDecision.Continue:
                        continue;
                    case UserDecision.Retry:
                        await _taskRepo.UpdateStatusAsync(task.Id, TaskItemStatus.Pending);
                        var retrySuccess = await ExecuteWithInterruptAsync(handler, session, phase, task, null, ct);
                        if (!retrySuccess)
                        {
                            await _taskRepo.UpdateStatusAsync(task.Id, TaskItemStatus.Failed);
                            return false;
                        }
                        break;
                }
            }

            await _taskRepo.UpdateStatusAsync(task.Id, TaskItemStatus.Completed);
        }

        return true;
    }

    private async Task<bool> ExecutePerSubtaskAsync(IPhaseHandler handler, Session session, Phase phase, CancellationToken ct)
    {
        var tasks = await _taskRepo.GetBySessionIdAsync(session.Id);
        var orderedTasks = tasks.OrderBy(t => t.Ordinal).ToList();

        foreach (var task in orderedTasks)
        {
            var subtasks = await _subtaskRepo.GetByTaskIdAsync(task.Id);
            var orderedSubtasks = subtasks.OrderBy(s => s.Ordinal).ToList();

            foreach (var subtask in orderedSubtasks)
            {
                if (subtask.Status == SubtaskItemStatus.Completed) continue;

                await _notifier.OnSubtaskStarted(subtask);
                await _subtaskRepo.UpdateStatusAsync(subtask.Id, SubtaskItemStatus.Running);

                var success = await ExecuteWithInterruptAsync(handler, session, phase, task, subtask, ct);

                if (!success)
                {
                    var decision = await _notifier.RequestUserDecision(
                        $"Subtask '{subtask.Title}' failed. What would you like to do?",
                        new[] { UserDecision.Abort, UserDecision.Continue, UserDecision.Retry });

                    switch (decision)
                    {
                        case UserDecision.Abort:
                            return false;
                        case UserDecision.Continue:
                            continue;
                        case UserDecision.Retry:
                            await _subtaskRepo.UpdateStatusAsync(subtask.Id, SubtaskItemStatus.Pending);
                            var retrySuccess = await ExecuteWithInterruptAsync(handler, session, phase, task, subtask, ct);
                            if (!retrySuccess) return false;
                            break;
                    }
                }
            }

            await TryCompleteTaskAsync(task);
        }

        return true;
    }

    private async Task TryCompleteTaskAsync(TaskItem task)
    {
        var subtasks = await _subtaskRepo.GetByTaskIdAsync(task.Id);
        var allCompleted = subtasks.All(s =>
            s.Status == SubtaskItemStatus.Completed || s.Status == SubtaskItemStatus.Skipped);

        if (allCompleted && subtasks.Count > 0)
            await _taskRepo.UpdateStatusAsync(task.Id, TaskItemStatus.Completed);
    }
}
