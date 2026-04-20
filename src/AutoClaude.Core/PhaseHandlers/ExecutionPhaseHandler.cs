using System.Text.Json;
using AutoClaude.Core.Domain;
using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;

namespace AutoClaude.Core.PhaseHandlers;

public class ExecutionPhaseHandler : IPhaseHandler
{
    private readonly ICliExecutor _cliExecutor;
    private readonly ISubtaskRepository _subtaskRepo;
    private readonly IExecutionRecordRepository _executionRepo;
    private readonly IOrchestrationNotifier _notifier;

    public PhaseType HandledPhase => PhaseType.Execution;

    public ExecutionPhaseHandler(
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

        await _notifier.OnExecutionStarted($"Executando: {subtask.Title}", subtask.Prompt);

        var prompt = subtask.Prompt;
        if (!string.IsNullOrEmpty(context.UserInstruction))
            prompt += "\n\nInstrucao adicional do usuario: " + context.UserInstruction;

        var effectiveWorkDir = subtask.WorkingDirectory ?? context.Session.TargetPath;

        var request = new CliRequest
        {
            SessionId = context.Session.Id,
            Prompt = prompt,
            WorkingDirectory = effectiveWorkDir,
            AllowedDirectories = context.Session.AllowedDirectories,
            AllowWrite = context.AllowWrite,
            ResumeSessionId = context.GetCliSessionId(effectiveWorkDir),
            OutputCallback = async line => await _notifier.OnCliOutputReceived(line),
            RetryCallback = async (attempt, delay, reason) =>
                await _notifier.OnRetryStarted(attempt, delay, reason),
            RetryExecutingCallback = async (attempt) =>
                await _notifier.OnRetryExecuting(attempt)
        };

        var result = await _cliExecutor.ExecuteAsync(request, ct);
        if (!string.IsNullOrEmpty(result.CliSessionId) && effectiveWorkDir != null)
            context.SetCliSessionId(effectiveWorkDir, result.CliSessionId);

        if (result.IsSuccess)
        {
            var responseText = AgentResponse.ExtractResult(result.StandardOutput, result.OutputJson);
            record.MarkSuccess(responseText, result.StandardOutput, result.ExitCode, result.DurationMs);
            await _executionRepo.UpdateAsync(record);
            await _notifier.OnExecutionCompleted(record);

            await _subtaskRepo.UpdateStatusAsync(subtask.Id, SubtaskItemStatus.Completed);
            await _subtaskRepo.UpdateResultSummaryAsync(subtask.Id, responseText);
            subtask.MarkCompleted(responseText);

            return PhaseResult.Succeeded(responseText);
        }

        record.MarkFailure(result.StandardError, result.ExitCode, result.DurationMs);
        await _executionRepo.UpdateAsync(record);
        await _notifier.OnExecutionCompleted(record);

        await _subtaskRepo.UpdateStatusAsync(subtask.Id, SubtaskItemStatus.Failed);
        await _subtaskRepo.UpdateResultSummaryAsync(subtask.Id, result.StandardError);
        subtask.MarkFailed(result.StandardError);

        return PhaseResult.Failed(result.StandardError);
    }

}
