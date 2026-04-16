using System.Text;
using System.Text.Json;
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
        await _notifier.OnExecutionStarted("Decompondo em macro tarefas...");

        var request = new CliRequest
        {
            Prompt = prompt,
            WorkingDirectory = context.Session.TargetPath,
            OutputCallback = line => _notifier.OnCliOutputReceived(line)
        };

        var result = await _cliExecutor.ExecuteAsync(request, ct);

        if (!result.IsSuccess)
        {
            record.MarkFailure(result.StandardError, result.ExitCode, result.DurationMs);
            await _executionRepo.UpdateAsync(record);
            await _notifier.OnExecutionCompleted(record);
            return PhaseResult.Failed(result.StandardError);
        }

        var responseText = ExtractResultFromJson(result.StandardOutput);
        record.MarkSuccess(responseText, result.StandardOutput, result.ExitCode, result.DurationMs);
        await _executionRepo.UpdateAsync(record);
        await _notifier.OnExecutionCompleted(record);

        var tasks = ParseTasks(responseText, context.Session.Id);
        if (tasks.Count == 0)
            return PhaseResult.Failed("Nenhuma tarefa foi gerada");

        // Show tasks and confirm with user
        var tasksSummary = new StringBuilder();
        foreach (var task in tasks)
            tasksSummary.AppendLine($"  {task.Ordinal}. {task.Title}\n     {task.Description}\n");

        var confirmed = await _notifier.ConfirmWithUser("Tarefas geradas", tasksSummary.ToString());

        if (!confirmed)
            return PhaseResult.Failed("Tarefas rejeitadas pelo usuario");

        foreach (var task in tasks)
            await _taskRepo.InsertAsync(task);

        return PhaseResult.Succeeded($"Criadas {tasks.Count} tarefas");
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
        try
        {
            var jsonStart = responseText.IndexOf('[');
            var jsonEnd = responseText.LastIndexOf(']');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonArray = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                using var doc = JsonDocument.Parse(jsonArray);
                var ordinal = 1;
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    tasks.Add(new TaskItem
                    {
                        SessionId = sessionId,
                        Title = element.GetProperty("title").GetString() ?? $"Task {ordinal}",
                        Description = element.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                        Ordinal = ordinal++
                    });
                }
            }
        }
        catch (JsonException) { }

        return tasks;
    }

    private static string ExtractResultFromJson(string jsonOutput)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            if (doc.RootElement.TryGetProperty("result", out var resultProp))
                return resultProp.GetString() ?? jsonOutput;
        }
        catch (JsonException) { }
        return jsonOutput;
    }
}
