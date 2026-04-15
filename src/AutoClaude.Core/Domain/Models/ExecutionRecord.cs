using AutoClaude.Core.Domain.Enums;

namespace AutoClaude.Core.Domain.Models;

public class ExecutionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? SubtaskId { get; set; }
    public Guid PhaseId { get; set; }
    public string CliType { get; set; } = "claude";
    public string PromptSent { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public string? ResponseText { get; set; }
    public string? ResponseJson { get; set; }
    public int? ExitCode { get; set; }
    public ExecutionOutcome Outcome { get; set; } = ExecutionOutcome.Pending;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? ErrorMessage { get; set; }

    public void MarkStarted()
    {
        StartedAt = DateTime.UtcNow;
        Outcome = ExecutionOutcome.Pending;
    }

    public void MarkSuccess(string responseText, string? responseJson, int exitCode, long durationMs)
    {
        CompletedAt = DateTime.UtcNow;
        ResponseText = responseText;
        ResponseJson = responseJson;
        ExitCode = exitCode;
        DurationMs = durationMs;
        Outcome = ExecutionOutcome.Success;
    }

    public void MarkFailure(string errorMessage, int? exitCode, long durationMs)
    {
        CompletedAt = DateTime.UtcNow;
        ErrorMessage = errorMessage;
        ExitCode = exitCode;
        DurationMs = durationMs;
        Outcome = ExecutionOutcome.Failure;
    }
}
