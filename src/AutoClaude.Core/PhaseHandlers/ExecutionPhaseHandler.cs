using System.Text.Json;
using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;

namespace AutoClaude.Core.PhaseHandlers;

public class ExecutionPhaseHandler : IPhaseHandler
{
    private readonly ICliExecutor _cliExecutor;
    private readonly ISubtaskRepository _subtaskRepo;
    private readonly IExecutionRecordRepository _executionRepo;

    public PhaseType HandledPhase => PhaseType.Execution;

    public ExecutionPhaseHandler(
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
            return PhaseResult.Failed("Execution requires a current subtask");

        var subtask = context.CurrentSubtask;

        var record = new ExecutionRecord
        {
            SessionId = context.Session.Id,
            TaskId = context.CurrentTask?.Id,
            SubtaskId = subtask.Id,
            PhaseId = context.Phase.Id,
            PromptSent = subtask.Prompt
        };
        record.MarkStarted();
        await _executionRepo.InsertAsync(record);

        var request = new CliRequest
        {
            Prompt = subtask.Prompt,
            WorkingDirectory = context.Session.TargetPath
        };

        var result = await _cliExecutor.ExecuteAsync(request, ct);

        if (result.IsSuccess)
        {
            var responseText = ExtractResultFromJson(result.StandardOutput);
            record.MarkSuccess(responseText, result.StandardOutput, result.ExitCode, result.DurationMs);
            await _executionRepo.UpdateAsync(record);

            await _subtaskRepo.UpdateStatusAsync(subtask.Id, SubtaskItemStatus.Completed);
            await _subtaskRepo.UpdateResultSummaryAsync(subtask.Id, responseText);
            subtask.MarkCompleted(responseText);

            return PhaseResult.Succeeded(responseText);
        }

        record.MarkFailure(result.StandardError, result.ExitCode, result.DurationMs);
        await _executionRepo.UpdateAsync(record);

        await _subtaskRepo.UpdateStatusAsync(subtask.Id, SubtaskItemStatus.Failed);
        await _subtaskRepo.UpdateResultSummaryAsync(subtask.Id, result.StandardError);
        subtask.MarkFailed(result.StandardError);

        return PhaseResult.Failed(result.StandardError);
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
