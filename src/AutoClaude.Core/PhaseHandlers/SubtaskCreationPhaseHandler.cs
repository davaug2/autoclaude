using System.Text.Json;
using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;

namespace AutoClaude.Core.PhaseHandlers;

public class SubtaskCreationPhaseHandler : IPhaseHandler
{
    private readonly ICliExecutor _cliExecutor;
    private readonly ISubtaskRepository _subtaskRepo;
    private readonly IExecutionRecordRepository _executionRepo;
    private readonly IOrchestrationNotifier _notifier;

    public PhaseType HandledPhase => PhaseType.SubtaskCreation;

    public SubtaskCreationPhaseHandler(
        ICliExecutor cliExecutor,
        ISubtaskRepository subtaskRepo,
        IExecutionRecordRepository executionRepo,
        IOrchestrationNotifier notifier)
    {
        _cliExecutor = cliExecutor;
        _subtaskRepo = subtaskRepo;
        _executionRepo = executionRepo;
        _notifier = notifier;
    }

    public async Task<PhaseResult> HandleAsync(PhaseContext context, CancellationToken ct = default)
    {
        if (context.CurrentTask == null)
            return PhaseResult.Failed("SubtaskCreation requires a current task");

        var prompt = BuildPrompt(context);

        var record = new ExecutionRecord
        {
            SessionId = context.Session.Id,
            TaskId = context.CurrentTask.Id,
            PhaseId = context.Phase.Id,
            PromptSent = prompt
        };
        record.MarkStarted();
        await _executionRepo.InsertAsync(record);

        await _notifier.OnExecutionStarted($"Criando subtarefas para: {context.CurrentTask.Title}");

        var request = new CliRequest
        {
            Prompt = prompt,
            SystemPrompt = context.Phase.SystemPrompt,
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

        var subtasks = ParseSubtasks(responseText, context.CurrentTask.Id, context.Session.Id);
        foreach (var subtask in subtasks)
            await _subtaskRepo.InsertAsync(subtask);

        return PhaseResult.Succeeded($"Created {subtasks.Count} subtasks for task '{context.CurrentTask.Title}'");
    }

    private string BuildPrompt(PhaseContext context)
    {
        var task = context.CurrentTask!;

        if (!string.IsNullOrEmpty(context.Phase.PromptTemplate))
        {
            return context.Phase.PromptTemplate
                .Replace("{{task_title}}", task.Title)
                .Replace("{{task_description}}", task.Description ?? "");
        }

        return $"For the following task, create subtasks with ready-to-execute prompts for Claude Code CLI.\n\nTask: {task.Title}\nDescription: {task.Description}\n\nReturn a JSON array: [{{\"title\": \"title\", \"prompt\": \"complete execution prompt\"}}]";
    }

    private static List<SubtaskItem> ParseSubtasks(string responseText, Guid taskId, Guid sessionId)
    {
        var subtasks = new List<SubtaskItem>();
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
                    subtasks.Add(new SubtaskItem
                    {
                        TaskId = taskId,
                        SessionId = sessionId,
                        Title = element.GetProperty("title").GetString() ?? $"Subtask {ordinal}",
                        Prompt = element.GetProperty("prompt").GetString() ?? "",
                        Ordinal = ordinal++
                    });
                }
            }
        }
        catch (JsonException) { }

        return subtasks;
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
