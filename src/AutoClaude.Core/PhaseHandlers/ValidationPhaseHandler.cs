using System.Text.Json;
using AutoClaude.Core.Domain;
using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;

namespace AutoClaude.Core.PhaseHandlers;

public class ValidationPhaseHandler : IPhaseHandler
{
    private readonly ICliExecutor _cliExecutor;
    private readonly ISubtaskRepository _subtaskRepo;
    private readonly IExecutionRecordRepository _executionRepo;
    private readonly IOrchestrationNotifier _notifier;

    public PhaseType HandledPhase => PhaseType.Validation;

    public ValidationPhaseHandler(
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

        await _notifier.OnExecutionStarted($"Validando: {subtask.Title}", prompt);

        var request = new CliRequest
        {
            Prompt = prompt,
            SystemPrompt = context.Phase.SystemPrompt,
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

        var responseText = AgentResponse.ExtractResult(result.StandardOutput, result.OutputJson);
        record.MarkSuccess(responseText, result.StandardOutput, result.ExitCode, result.DurationMs);
        await _executionRepo.UpdateAsync(record);
        await _notifier.OnExecutionCompleted(record);

        var (isValid, note) = ParseValidation(responseText, result.OutputJson);
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

        return $"Validate if the following subtask was completed correctly.\n\nSubtask: {subtask.Title}\nOriginal prompt: {subtask.Prompt}\nResult: {subtask.ResultSummary}\n\nWrite the output JSON file with: {{\"valid\": true/false, \"note\": \"observation\"}}";
    }

    private static (bool isValid, string note) ParseValidation(string responseText, string? jsonFileContent)
    {
        foreach (var block in AgentResponse.ExtractJsonBlocks(responseText, jsonFileContent))
        {
            try
            {
                using var doc = JsonDocument.Parse(block);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("valid", out var validProp))
                {
                    var valid = validProp.GetBoolean();
                    var note = doc.RootElement.TryGetProperty("note", out var noteProp) ? noteProp.GetString() ?? "" : "";
                    return (valid, note);
                }
            }
            catch (JsonException) { }
        }

        return (true, responseText);
    }

}
