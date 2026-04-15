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

    public PhaseType HandledPhase => PhaseType.Decomposition;

    public DecompositionPhaseHandler(
        ICliExecutor cliExecutor,
        ISessionRepository sessionRepo,
        ITaskRepository taskRepo,
        IExecutionRecordRepository executionRepo)
    {
        _cliExecutor = cliExecutor;
        _sessionRepo = sessionRepo;
        _taskRepo = taskRepo;
        _executionRepo = executionRepo;
    }

    public async Task<PhaseResult> HandleAsync(PhaseContext context, CancellationToken ct = default)
    {
        var prompt = BuildPrompt(context);

        var record = new ExecutionRecord
        {
            SessionId = context.Session.Id,
            PhaseId = context.Phase.Id,
            PromptSent = prompt,
            SystemPrompt = context.Phase.SystemPrompt
        };
        record.MarkStarted();
        await _executionRepo.InsertAsync(record);

        var request = new CliRequest
        {
            Prompt = prompt,
            SystemPrompt = context.Phase.SystemPrompt,
            WorkingDirectory = context.Session.TargetPath
        };

        var result = await _cliExecutor.ExecuteAsync(request, ct);

        if (!result.IsSuccess)
        {
            record.MarkFailure(result.StandardError, result.ExitCode, result.DurationMs);
            await _executionRepo.UpdateAsync(record);
            return PhaseResult.Failed(result.StandardError);
        }

        var responseText = ExtractResultFromJson(result.StandardOutput);
        record.MarkSuccess(responseText, result.StandardOutput, result.ExitCode, result.DurationMs);
        await _executionRepo.UpdateAsync(record);

        var tasks = ParseTasks(responseText, context.Session.Id);
        foreach (var task in tasks)
            await _taskRepo.InsertAsync(task);

        return PhaseResult.Succeeded($"Created {tasks.Count} tasks");
    }

    private string BuildPrompt(PhaseContext context)
    {
        var analysisResult = "";
        try
        {
            using var doc = JsonDocument.Parse(context.Session.ContextJson);
            if (doc.RootElement.TryGetProperty("analysis_result", out var prop))
                analysisResult = prop.GetString() ?? "";
        }
        catch (JsonException) { }

        if (!string.IsNullOrEmpty(context.Phase.PromptTemplate))
        {
            return context.Phase.PromptTemplate
                .Replace("{{analysis_result}}", analysisResult);
        }

        return $"Based on the following specification, decompose into ordered macro tasks.\n\nSpecification: {analysisResult}\n\nReturn a JSON array: [{{\"title\": \"title\", \"description\": \"detailed description\"}}]";
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
