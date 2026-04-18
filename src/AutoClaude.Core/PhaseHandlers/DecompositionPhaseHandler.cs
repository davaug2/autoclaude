using System.Text;
using System.Text.Json;
using AutoClaude.Core.Domain;
using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;

namespace AutoClaude.Core.PhaseHandlers;

public class DecompositionPhaseHandler : IPhaseHandler
{
    private readonly ICliExecutor _cliExecutor;
    private readonly ISessionRepository _sessionRepo;
    private readonly ITaskRepository _taskRepo;
    private readonly IExecutionRecordRepository _executionRepo;
    private readonly IOrchestrationNotifier _notifier;

    public PhaseType HandledPhase => PhaseType.Decomposition;

    public DecompositionPhaseHandler(
        ICliExecutor cliExecutor,
        ISessionRepository sessionRepo,
        ITaskRepository taskRepo,
        IExecutionRecordRepository executionRepo,
        IOrchestrationNotifier notifier)
    {
        _cliExecutor = cliExecutor;
        _sessionRepo = sessionRepo;
        _taskRepo = taskRepo;
        _executionRepo = executionRepo;
        _notifier = notifier;
    }

    public async Task<PhaseResult> HandleAsync(PhaseContext context, CancellationToken ct = default)
    {
        var analysisResult = ExtractAnalysisResult(context.Session.ContextJson);
        var prompt = $"Com base na seguinte especificacao, decomponha em macro tarefas ordenadas.\n\n" +
                     $"Especificacao: {analysisResult}\n\n" +
                     "Retorne um JSON array: [{\"title\": \"titulo\", \"description\": \"descricao detalhada\"}]";

        var record = new ExecutionRecord
        {
            SessionId = context.Session.Id,
            PhaseId = context.Phase.Id,
            PromptSent = prompt
        };
        record.MarkStarted();
        await _executionRepo.InsertAsync(record);
        await _notifier.OnExecutionStarted("Decompondo em macro tarefas...", prompt);

        var request = new CliRequest
        {
            Prompt = prompt,
            WorkingDirectory = context.Session.TargetPath,
            AllowedDirectories = context.Session.AllowedDirectories,
            AllowWrite = context.AllowWrite,
            ResumeSessionId = context.CliSessionId,
            OutputCallback = async line => await _notifier.OnCliOutputReceived(line),
            RetryCallback = async (attempt, delay, reason) =>
                await _notifier.OnRetryStarted(attempt, delay, reason),
            RetryExecutingCallback = async (attempt) =>
                await _notifier.OnRetryExecuting(attempt)
        };

        var result = await _cliExecutor.ExecuteAsync(request, ct);
        if (!string.IsNullOrEmpty(result.CliSessionId))
            context.CliSessionId = result.CliSessionId;

        if (!result.IsSuccess)
        {
            record.MarkFailure(result.StandardError, result.ExitCode, result.DurationMs);
            await _executionRepo.UpdateAsync(record);
            await _notifier.OnExecutionCompleted(record);
            return PhaseResult.Failed(result.StandardError);
        }

        var responseText = AgentResponse.ExtractResult(result.StandardOutput);
        record.MarkSuccess(responseText, result.StandardOutput, result.ExitCode, result.DurationMs);
        await _executionRepo.UpdateAsync(record);
        await _notifier.OnExecutionCompleted(record);

        var currentResponse = responseText;
        while (true)
        {
            var tasks = ParseTasks(currentResponse, context.Session.Id);
            if (tasks.Count == 0)
                return PhaseResult.Failed("Nenhuma tarefa foi gerada");

            var tasksSummary = new StringBuilder();
            foreach (var task in tasks)
                tasksSummary.AppendLine($"  {task.Ordinal}. {task.Title}\n     {task.Description}\n");

            var (confirmation, modification) = await _notifier.ConfirmWithUser("Tarefas geradas", tasksSummary.ToString());

            if (confirmation == Domain.Enums.ConfirmationResult.Reject)
                return PhaseResult.Failed("Tarefas rejeitadas pelo usuario");

            if (confirmation == Domain.Enums.ConfirmationResult.GoBack)
                throw new GoBackException();

            if (confirmation == Domain.Enums.ConfirmationResult.Confirm)
            {
                foreach (var task in tasks)
                    await _taskRepo.InsertAsync(task);
                return PhaseResult.Succeeded($"Criadas {tasks.Count} tarefas");
            }

            // Modify
            context.Memory.AddTemporary("Modificacao nas tarefas", modification!);
            await context.SaveMemoryAsync();
            var modRecord = new ExecutionRecord
            {
                SessionId = context.Session.Id, PhaseId = context.Phase.Id,
                PromptSent = $"Modifique as tarefas: {modification}"
            };
            modRecord.MarkStarted();
            await _executionRepo.InsertAsync(modRecord);
            await _notifier.OnExecutionStarted("Modificando tarefas...", $"Modifique as tarefas: {modification}");

            var modRequest = new CliRequest
            {
                Prompt = $"O usuario pediu modificacoes nas tarefas.\n\n" +
                         $"Tarefas atuais:\n{tasksSummary}\n\n" +
                         $"Modificacao: {modification}\n" +
                         context.Memory.ToPromptText() + "\n\n" +
                         "Retorne o JSON array atualizado: [{\"title\": \"titulo\", \"description\": \"descricao\"}]",
                WorkingDirectory = context.Session.TargetPath,
            AllowedDirectories = context.Session.AllowedDirectories,
            AllowWrite = context.AllowWrite,
                OutputCallback = async line => await _notifier.OnCliOutputReceived(line),
            RetryCallback = async (attempt, delay, reason) =>
                await _notifier.OnRetryStarted(attempt, delay, reason),
            RetryExecutingCallback = async (attempt) =>
                await _notifier.OnRetryExecuting(attempt)
            };

            var modResult = await _cliExecutor.ExecuteAsync(modRequest, ct);
            currentResponse = AgentResponse.ExtractResult(modResult.StandardOutput);

            if (modResult.IsSuccess)
                modRecord.MarkSuccess(currentResponse, modResult.StandardOutput, modResult.ExitCode, modResult.DurationMs);
            else
                modRecord.MarkFailure(modResult.StandardError, modResult.ExitCode, modResult.DurationMs);

            await _executionRepo.UpdateAsync(modRecord);
            await _notifier.OnExecutionCompleted(modRecord);

            if (!modResult.IsSuccess)
                return PhaseResult.Failed(modResult.StandardError);
        }
    }

    private static string ExtractAnalysisResult(string contextJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(contextJson);
            if (doc.RootElement.TryGetProperty("analysis_result", out var prop))
                return prop.GetString() ?? "";
        }
        catch (JsonException) { }
        return "";
    }

    private static List<TaskItem> ParseTasks(string responseText, Guid sessionId)
    {
        var tasks = new List<TaskItem>();

        foreach (var block in AgentResponse.ExtractJsonBlocks(responseText))
        {
            try
            {
                using var doc = JsonDocument.Parse(block);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                var ordinal = 1;
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty("title", out _)) continue;

                    tasks.Add(new TaskItem
                    {
                        SessionId = sessionId,
                        Title = element.GetProperty("title").GetString() ?? $"Task {ordinal}",
                        Description = element.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                        Ordinal = ordinal++
                    });
                }

                if (tasks.Count > 0) break;
            }
            catch (JsonException) { }
        }

        return tasks;
    }

}
