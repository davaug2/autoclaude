using System.Text.Json;
using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;

namespace AutoClaude.Core.PhaseHandlers;

public class ValidationPhaseHandler : IPhaseHandler
{
    private readonly ICliExecutor _cliExecutor;
    private readonly ISubtaskRepository _subtaskRepo;
    private readonly IExecutionRecordRepository _executionRepo;

    public PhaseType HandledPhase => PhaseType.Validation;

    public ValidationPhaseHandler(
        ICliExecutor cliExecutor,
        ISubtaskRepository subtaskRepo,
        IExecutionRecordRepository executionRepo)
    {
        _cliExecutor = cliExecutor;
        _subtaskRepo = subtaskRepo;
        _executionRepo = executionRepo;
    }

    public async Task<PhaseResult> HandleAsync(PhaseContext context, CancellationToken ct = default)
    {
        if (context.CurrentSubtask == null)
            return PhaseResult.Failed("Validation requires a current subtask");

        var subtask = context.CurrentSubtask;
        var prompt = BuildPrompt(context);

        var record = new ExecutionRecord
        {
            SessionId = context.Session.Id,
            TaskId = context.CurrentTask?.Id,
            SubtaskId = subtask.Id,
            PhaseId = context.Phase.Id,
            PromptSent = prompt
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

        var (isValid, note) = ParseValidation(responseText);
        await _subtaskRepo.UpdateValidationNoteAsync(subtask.Id, note);
        subtask.SetValidation(note);

        if (isValid)
        {
            await _subtaskRepo.UpdateStatusAsync(subtask.Id, SubtaskItemStatus.Completed);
            return PhaseResult.Succeeded(note);
        }

        await _subtaskRepo.UpdateStatusAsync(subtask.Id, SubtaskItemStatus.Failed);
        return PhaseResult.Failed(note);
    }

    private string BuildPrompt(PhaseContext context)
    {
        var subtask = context.CurrentSubtask!;

        if (!string.IsNullOrEmpty(context.Phase.PromptTemplate))
        {
            return context.Phase.PromptTemplate
                .Replace("{{subtask_title}}", subtask.Title)
                .Replace("{{subtask_prompt}}", subtask.Prompt)
                .Replace("{{subtask_result}}", subtask.ResultSummary ?? "");
        }

        return $"Validate if the following subtask was completed correctly.\n\nSubtask: {subtask.Title}\nOriginal prompt: {subtask.Prompt}\nResult: {subtask.ResultSummary}\n\nReturn a JSON: {{\"valid\": true/false, \"note\": \"observation\"}}";
    }

    private static (bool isValid, string note) ParseValidation(string responseText)
    {
        try
        {
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                using var doc = JsonDocument.Parse(json);
                var valid = doc.RootElement.TryGetProperty("valid", out var validProp) && validProp.GetBoolean();
                var note = doc.RootElement.TryGetProperty("note", out var noteProp) ? noteProp.GetString() ?? "" : "";
                return (valid, note);
            }
        }
        catch (JsonException) { }

        return (true, responseText);
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
