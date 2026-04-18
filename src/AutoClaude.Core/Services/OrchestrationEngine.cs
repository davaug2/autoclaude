using System.Text.Json;
using AutoClaude.Core.Domain;
using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.PhaseHandlers;
using AutoClaude.Core.Ports;
using Microsoft.Extensions.Logging;

namespace AutoClaude.Core.Services;

public class OrchestrationEngine
{
    private readonly IPhaseRepository _phaseRepo;
    private readonly ITaskRepository _taskRepo;
    private readonly ISubtaskRepository _subtaskRepo;
    private readonly ISessionRepository _sessionRepo;
    private readonly IExecutionRecordRepository _executionRepo;
    private readonly ICliExecutor _cliExecutor;
    private readonly PhaseHandlerFactory _handlerFactory;
    private readonly IOrchestrationNotifier _notifier;
    private readonly ILogger<OrchestrationEngine> _logger;

    public OrchestrationEngine(
        IPhaseRepository phaseRepo,
        ITaskRepository taskRepo,
        ISubtaskRepository subtaskRepo,
        ISessionRepository sessionRepo,
        IExecutionRecordRepository executionRepo,
        ICliExecutor cliExecutor,
        PhaseHandlerFactory handlerFactory,
        IOrchestrationNotifier notifier,
        ILogger<OrchestrationEngine> logger)
    {
        _phaseRepo = phaseRepo;
        _taskRepo = taskRepo;
        _subtaskRepo = subtaskRepo;
        _sessionRepo = sessionRepo;
        _executionRepo = executionRepo;
        _cliExecutor = cliExecutor;
        _handlerFactory = handlerFactory;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task RunAsync(Session session, CancellationToken ct = default)
    {
        session.UpdateStatus(SessionStatus.Running);
        await _sessionRepo.UpdateStatusAsync(session.Id, SessionStatus.Running);

        // Extract directory paths from the objective and add as allowed directories
        var dirsBefore = session.AllowedDirectories.Count;
        EnsureAllowedDirectories(session);
        if (session.AllowedDirectories.Count > dirsBefore)
        {
            var contextJson = SessionContextJson.MergeAllowedDirectories(session.ContextJson, session.AllowedDirectories);
            session.ContextJson = contextJson;
            await _sessionRepo.UpdateContextAsync(session.Id, contextJson);
        }

        var memory = LoadMemory(session);
        var cliSessionMap = SessionContextJson.HydrateCliSessionMap(session.ContextJson);

        var phases = await _phaseRepo.GetByWorkModelIdAsync(session.WorkModelId);
        var orderedPhases = phases.OrderBy(p => p.Ordinal).ToList();

        var i = 0;
        while (i < orderedPhases.Count)
        {
            var phase = orderedPhases[i];

            if (phase.Ordinal <= session.CurrentPhaseOrdinal)
            {
                i++;
                continue;
            }

            memory.ClearTemporary();

            var allowWrite = phase.PhaseType == Domain.Enums.PhaseType.Execution;
            var handler = _handlerFactory.GetHandler(phase.PhaseType);
            await _notifier.OnPhaseStarted(phase, session);

            bool phaseSuccess;

            try
            {
                switch (phase.RepeatMode)
                {
                    case RepeatMode.Once:
                        phaseSuccess = await ExecuteWithInterruptAsync(handler, session, phase, null, null, memory, cliSessionMap, allowWrite, ct);
                        break;
                    case RepeatMode.PerTask:
                        phaseSuccess = await ExecutePerTaskAsync(handler, session, phase, memory, cliSessionMap, allowWrite, ct);
                        break;
                    case RepeatMode.PerSubtask:
                        phaseSuccess = await ExecutePerSubtaskAsync(handler, session, phase, memory, cliSessionMap, allowWrite, ct);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown repeat mode: {phase.RepeatMode}");
                }
            }
            catch (GoBackException goBack)
            {
                var targetIndex = ResolveGoBackIndex(goBack.TargetPhase, i, orderedPhases);
                if (targetIndex >= 0 && targetIndex < i)
                {
                    var rewindPhase = orderedPhases[targetIndex];
                    await _notifier.OnPhaseCompleted(phase, false, $"Voltando para fase '{rewindPhase.Name}'");
                    await CleanupForPhaseRewindAsync(session, rewindPhase, orderedPhases);
                    cliSessionMap.Clear();
                    await SaveCliSessionMap(session, cliSessionMap);
                    session.AdvancePhase(rewindPhase.Ordinal - 1);
                    await _sessionRepo.UpdateCurrentPhaseOrdinalAsync(session.Id, rewindPhase.Ordinal - 1);
                    i = targetIndex;
                }
                else
                {
                    await _notifier.OnPhaseCompleted(phase, false, "Voltando para fase anterior");
                    if (i > 0)
                    {
                        i--;
                        var rewindPhase = orderedPhases[i];
                        await CleanupForPhaseRewindAsync(session, rewindPhase, orderedPhases);
                        cliSessionMap.Clear();
                        await SaveCliSessionMap(session, cliSessionMap);
                        session.AdvancePhase(rewindPhase.Ordinal - 1);
                        await _sessionRepo.UpdateCurrentPhaseOrdinalAsync(session.Id, rewindPhase.Ordinal - 1);
                    }
                }
                continue;
            }

            // After SubtaskCreation completes, confirm all tasks+subtasks before advancing
            if (phaseSuccess && phase.PhaseType == PhaseType.SubtaskCreation)
            {
                phaseSuccess = await ConfirmAllSubtasksAsync(session);
                if (!phaseSuccess)
                {
                    // User rejected — delete all subtasks and reset tasks so the phase can re-run
                    await _subtaskRepo.DeleteBySessionIdAsync(session.Id);
                    var allTasks = await _taskRepo.GetBySessionIdAsync(session.Id);
                    foreach (var t in allTasks)
                        await _taskRepo.UpdateStatusAsync(t.Id, TaskItemStatus.Pending);
                }
            }

            await SaveMemory(session, memory);
            await _notifier.OnPhaseCompleted(phase, phaseSuccess);

            if (!phaseSuccess)
            {
                var decision = await _notifier.RequestUserDecision(
                    $"A fase '{phase.Name}' falhou. O que deseja fazer?",
                    new[] { UserDecision.Continue, UserDecision.Retry, UserDecision.Pause, UserDecision.Abort });

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
            i++;
        }

        session.UpdateStatus(SessionStatus.Completed);
        await _sessionRepo.UpdateStatusAsync(session.Id, SessionStatus.Completed);
    }

    private async Task<bool> ExecuteWithInterruptAsync(
        IPhaseHandler handler, Session session, Phase phase,
        TaskItem? task, SubtaskItem? subtask, SessionMemory memory,
        Dictionary<string, string> cliSessionMap, bool allowWrite, CancellationToken ct)
    {
        var context = new PhaseContext
        {
            Session = session, Phase = phase,
            CurrentTask = task, CurrentSubtask = subtask,
            Memory = memory, AllowWrite = allowWrite,
            CliSessionMap = cliSessionMap,
            SaveMemoryAsync = () => SaveMemory(session, memory)
        };

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            _notifier.ResetInterruptToken();
            var interruptToken = _notifier.CreateInterruptToken();

            try
            {
                var result = await handler.HandleAsync(context, interruptToken);

                // Persist CLI session map if changed
                await SaveCliSessionMap(session, cliSessionMap);

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

                var intent = await InterpretUserIntentAsync(userInput, phase, session, ct);

                switch (intent.Action)
                {
                    case "go_back":
                        throw new GoBackException(intent.TargetPhase);
                    case "abort":
                        session.UpdateStatus(SessionStatus.Paused);
                        await _sessionRepo.UpdateStatusAsync(session.Id, SessionStatus.Paused);
                        return false;
                    default:
                        // Save user instruction to persistent memory
                        memory.AddPersistent("Instrucao do usuario durante " + phase.Name, userInput);
                        await SaveMemory(session, memory);

                        context = new PhaseContext
                        {
                            Session = session, Phase = phase,
                            CurrentTask = task, CurrentSubtask = subtask,
                            UserInstruction = userInput,
                            Memory = memory, AllowWrite = allowWrite,
                            CliSessionMap = cliSessionMap,
                            SaveMemoryAsync = () => SaveMemory(session, memory)
                        };
                        break;
                }
            }
        }
    }

    private async Task<bool> ExecutePerTaskAsync(IPhaseHandler handler, Session session, Phase phase, SessionMemory memory, Dictionary<string, string> cliSessionMap, bool allowWrite, CancellationToken ct)
    {
        var tasks = await _taskRepo.GetBySessionIdAsync(session.Id);
        var orderedTasks = tasks.OrderBy(t => t.Ordinal).ToList();

        foreach (var task in orderedTasks)
        {
            if (task.Status == TaskItemStatus.Completed) continue;

            await _notifier.OnTaskStarted(task);
            await _taskRepo.UpdateStatusAsync(task.Id, TaskItemStatus.InProgress);

            var success = await ExecuteWithInterruptAsync(handler, session, phase, task, null, memory, cliSessionMap, allowWrite, ct);

            if (!success)
            {
                await _taskRepo.UpdateStatusAsync(task.Id, TaskItemStatus.Failed);

                var decision = await _notifier.RequestUserDecision(
                    $"A tarefa '{task.Title}' falhou. O que deseja fazer?",
                    new[] { UserDecision.Continue, UserDecision.Retry, UserDecision.Pause, UserDecision.Abort });

                switch (decision)
                {
                    case UserDecision.Abort:
                        return false;
                    case UserDecision.Pause:
                        session.UpdateStatus(SessionStatus.Paused);
                        await _sessionRepo.UpdateStatusAsync(session.Id, SessionStatus.Paused);
                        return false;
                    case UserDecision.Continue:
                        continue;
                    case UserDecision.Retry:
                        await _taskRepo.UpdateStatusAsync(task.Id, TaskItemStatus.Pending);
                        var retrySuccess = await ExecuteWithInterruptAsync(handler, session, phase, task, null, memory, cliSessionMap, allowWrite, ct);
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

    private async Task<bool> ExecutePerSubtaskAsync(IPhaseHandler handler, Session session, Phase phase, SessionMemory memory, Dictionary<string, string> cliSessionMap, bool allowWrite, CancellationToken ct)
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

                var success = await ExecuteWithInterruptAsync(handler, session, phase, task, subtask, memory, cliSessionMap, allowWrite, ct);

                if (!success)
                {
                    var decision = await _notifier.RequestUserDecision(
                        $"A subtarefa '{subtask.Title}' falhou. O que deseja fazer?",
                        new[] { UserDecision.Continue, UserDecision.Retry, UserDecision.Pause, UserDecision.Abort });

                    switch (decision)
                    {
                        case UserDecision.Abort:
                            return false;
                        case UserDecision.Pause:
                            session.UpdateStatus(SessionStatus.Paused);
                            await _sessionRepo.UpdateStatusAsync(session.Id, SessionStatus.Paused);
                            return false;
                        case UserDecision.Continue:
                            continue;
                        case UserDecision.Retry:
                            await _subtaskRepo.UpdateStatusAsync(subtask.Id, SubtaskItemStatus.Pending);
                            var retrySuccess = await ExecuteWithInterruptAsync(handler, session, phase, task, subtask, memory, cliSessionMap, allowWrite, ct);
                            if (!retrySuccess) return false;
                            break;
                    }
                }
            }

            await TryCompleteTaskAsync(task);
        }

        return true;
    }

    private record UserIntent(string Action, string? Instruction, string? TargetPhase);

    private async Task<UserIntent> InterpretUserIntentAsync(
        string userInput, Phase currentPhase, Session session, CancellationToken ct)
    {
        var prompt = $"O usuario interrompeu a execucao durante a fase '{currentPhase.Name}' e digitou:\n\n" +
                     $"\"{userInput}\"\n\n" +
                     $"Objetivo da sessao: {session.Objective}\n\n" +
                     "Classifique a intencao do usuario e retorne APENAS um JSON:\n" +
                     "{{\"action\": \"go_back|continue|abort\", \"instruction\": \"texto original do usuario\", \"target_phase\": \"nome da fase alvo se go_back\"}}\n\n" +
                     "Regras de classificacao:\n" +
                     "- go_back: SOMENTE se o usuario pedir explicitamente para voltar/refazer fase anterior (ex: 'volte para o objetivo', 'refaca a analise', 'volte para tarefas'). " +
                     "Preencha target_phase com a fase mencionada (ex: 'analise', 'tarefas', 'subtarefas', 'execucao', 'validacao'). Se nao mencionar fase especifica, deixe target_phase null.\n" +
                     "- abort: SOMENTE se o usuario pedir para parar/cancelar (ex: 'pare', 'cancele')\n" +
                     "- continue: QUALQUER outro caso — se o usuario deu uma instrucao, requisito, detalhe tecnico ou informacao adicional, e continue. " +
                     "Copie o texto original do usuario no campo instruction.\n\n" +
                     "Na grande maioria dos casos a resposta sera continue.";

        await _notifier.OnInterpretingUserIntentStarted();
        try
        {
            try
            {
                var request = new CliRequest
                {
                    Prompt = prompt,
                    WorkingDirectory = session.TargetPath,
                    TimeoutSeconds = 30
                };

                var result = await _cliExecutor.ExecuteAsync(request, ct);
                if (result.IsSuccess)
                {
                    var responseText = AgentResponse.ExtractResult(result.StandardOutput);
                    return ParseIntent(responseText, userInput);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao interpretar intencao do usuario via CLI");
            }

            return new UserIntent("continue", userInput, null);
        }
        finally
        {
            await _notifier.OnInterpretingUserIntentCompleted();
        }
    }

    private static UserIntent ParseIntent(string responseText, string fallbackInstruction)
    {
        foreach (var block in AgentResponse.ExtractJsonBlocks(responseText))
        {
            try
            {
                using var doc = JsonDocument.Parse(block);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("action", out var a))
                {
                    var action = a.GetString() ?? "continue";
                    var instruction = doc.RootElement.TryGetProperty("instruction", out var ins) ? ins.GetString() : null;
                    var targetPhase = doc.RootElement.TryGetProperty("target_phase", out var tp) ? tp.GetString() : null;
                    return new UserIntent(action, instruction, targetPhase);
                }
            }
            catch (JsonException) { }
        }

        return new UserIntent("continue", fallbackInstruction, null);
    }

    private static int ResolveGoBackIndex(string? targetPhase, int currentIndex, List<Phase> orderedPhases)
    {
        if (string.IsNullOrWhiteSpace(targetPhase))
            return currentIndex - 1;

        var target = targetPhase.Trim().ToLowerInvariant();

        // Map common user expressions to PhaseType
        var phaseTypeMap = new Dictionary<string, PhaseType>(StringComparer.OrdinalIgnoreCase)
        {
            ["analysis"] = PhaseType.Analysis,
            ["analise"] = PhaseType.Analysis,
            ["análise"] = PhaseType.Analysis,
            ["objetivo"] = PhaseType.Analysis,
            ["decomposition"] = PhaseType.Decomposition,
            ["decomposicao"] = PhaseType.Decomposition,
            ["decomposição"] = PhaseType.Decomposition,
            ["tarefas"] = PhaseType.Decomposition,
            ["definicao de tarefas"] = PhaseType.Decomposition,
            ["definição de tarefas"] = PhaseType.Decomposition,
            ["subtaskcreation"] = PhaseType.SubtaskCreation,
            ["subtarefas"] = PhaseType.SubtaskCreation,
            ["criacao de subtarefas"] = PhaseType.SubtaskCreation,
            ["criação de subtarefas"] = PhaseType.SubtaskCreation,
            ["execution"] = PhaseType.Execution,
            ["execucao"] = PhaseType.Execution,
            ["execução"] = PhaseType.Execution,
            ["validation"] = PhaseType.Validation,
            ["validacao"] = PhaseType.Validation,
            ["validação"] = PhaseType.Validation,
        };

        // Try exact match on user expression
        if (phaseTypeMap.TryGetValue(target, out var phaseType))
        {
            for (var idx = 0; idx < currentIndex; idx++)
            {
                if (orderedPhases[idx].PhaseType == phaseType)
                    return idx;
            }
        }

        // Try matching against phase names (fuzzy: contains)
        for (var idx = 0; idx < currentIndex; idx++)
        {
            var phaseName = orderedPhases[idx].Name.ToLowerInvariant();
            if (phaseName.Contains(target) || target.Contains(phaseName))
                return idx;
        }

        // Fallback: previous phase
        return currentIndex - 1;
    }

    private async Task CleanupForPhaseRewindAsync(Session session, Phase targetPhase, List<Phase> orderedPhases)
    {
        // Determine which phase types will be re-executed (target and all after it)
        var phasesToRerun = orderedPhases
            .Where(p => p.Ordinal >= targetPhase.Ordinal)
            .Select(p => p.PhaseType)
            .ToHashSet();

        // Always delete execution records — they will be regenerated
        await _executionRepo.DeleteBySessionIdAsync(session.Id);

        if (phasesToRerun.Contains(PhaseType.Decomposition))
        {
            // Decomposition creates tasks; cascade delete subtasks and tasks
            await _subtaskRepo.DeleteBySessionIdAsync(session.Id);
            await _taskRepo.DeleteBySessionIdAsync(session.Id);
            return;
        }

        if (phasesToRerun.Contains(PhaseType.SubtaskCreation))
        {
            // SubtaskCreation creates subtasks; delete them and reset tasks to Pending
            await _subtaskRepo.DeleteBySessionIdAsync(session.Id);
            var tasks = await _taskRepo.GetBySessionIdAsync(session.Id);
            foreach (var t in tasks)
                await _taskRepo.UpdateStatusAsync(t.Id, TaskItemStatus.Pending);
            return;
        }

        if (phasesToRerun.Contains(PhaseType.Execution))
        {
            // Execution updates statuses; reset subtasks and tasks to Pending
            var subtasks = await _subtaskRepo.GetBySessionIdAsync(session.Id);
            foreach (var s in subtasks)
                await _subtaskRepo.UpdateStatusAsync(s.Id, SubtaskItemStatus.Pending);
            var tasks = await _taskRepo.GetBySessionIdAsync(session.Id);
            foreach (var t in tasks)
                await _taskRepo.UpdateStatusAsync(t.Id, TaskItemStatus.Pending);
            return;
        }

        if (phasesToRerun.Contains(PhaseType.Validation))
        {
            // Validation adds notes; clear them
            var subtasks = await _subtaskRepo.GetBySessionIdAsync(session.Id);
            foreach (var s in subtasks)
                await _subtaskRepo.UpdateValidationNoteAsync(s.Id, "");
        }
    }

    private static void EnsureAllowedDirectories(Session session)
    {
        var dirs = new HashSet<string>(session.AllowedDirectories, StringComparer.OrdinalIgnoreCase);

        // Always include TargetPath
        if (!string.IsNullOrWhiteSpace(session.TargetPath))
            dirs.Add(session.TargetPath);

        // Extract paths mentioned in the objective (Windows and Unix)
        if (!string.IsNullOrWhiteSpace(session.Objective))
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(
                session.Objective,
                @"[a-zA-Z]:\\[^\s,;""']+|/[a-zA-Z][^\s,;""']*(?:/[^\s,;""']+)+");

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var path = match.Value.TrimEnd('.', ',', ';', ')');
                if (Directory.Exists(path))
                    dirs.Add(path);
            }
        }

        session.AllowedDirectories = dirs.ToList();
    }

    private static SessionMemory LoadMemory(Session session)
    {
        try
        {
            using var doc = JsonDocument.Parse(session.ContextJson);
            if (doc.RootElement.TryGetProperty("memory", out var memProp))
            {
                var memory = JsonSerializer.Deserialize<SessionMemory>(memProp.GetRawText());
                if (memory != null) return memory;
            }
        }
        catch (JsonException) { }

        return new SessionMemory();
    }

    private async Task SaveCliSessionMap(Session session, Dictionary<string, string> cliSessionMap)
    {
        var contextJson = SessionContextJson.MergeCliSessionMap(session.ContextJson, cliSessionMap);
        if (contextJson != session.ContextJson)
        {
            session.ContextJson = contextJson;
            await _sessionRepo.UpdateContextAsync(session.Id, contextJson);
        }
    }

    private async Task SaveMemory(Session session, SessionMemory memory)
    {
        var contextDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(session.ContextJson)
            ?? new Dictionary<string, JsonElement>();
        var memoryJson = JsonSerializer.SerializeToElement(memory);
        contextDict["memory"] = memoryJson;
        session.ContextJson = JsonSerializer.Serialize(contextDict);
        await _sessionRepo.UpdateContextAsync(session.Id, session.ContextJson);
    }

    private async Task<bool> ConfirmAllSubtasksAsync(Session session)
    {
        var tasks = (await _taskRepo.GetBySessionIdAsync(session.Id)).OrderBy(t => t.Ordinal).ToList();
        var sb = new System.Text.StringBuilder();

        foreach (var task in tasks)
        {
            sb.AppendLine($"Tarefa {task.Ordinal}: {task.Title}");
            var subtasks = (await _subtaskRepo.GetByTaskIdAsync(task.Id)).OrderBy(s => s.Ordinal).ToList();
            foreach (var sub in subtasks)
            {
                sb.Append($"  {sub.Ordinal}. {sub.Title}");
                if (!string.IsNullOrEmpty(sub.WorkingDirectory))
                    sb.Append($"  [{sub.WorkingDirectory}]");
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        var (confirmation, _) = await _notifier.ConfirmWithUser("Todas as subtarefas geradas", sb.ToString());

        return confirmation == ConfirmationResult.Confirm || confirmation == ConfirmationResult.Modify;
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
