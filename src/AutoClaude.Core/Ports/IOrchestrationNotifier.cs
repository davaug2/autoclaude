using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;

namespace AutoClaude.Core.Ports;

public interface IOrchestrationNotifier
{
    Task OnPhaseStarted(Phase phase, Session session);
    Task OnPhaseCompleted(Phase phase, bool success, string? errorMessage = null);
    Task OnTaskStarted(TaskItem task);
    Task OnSubtaskStarted(SubtaskItem subtask);
    Task OnExecutionStarted(string description);
    Task OnCliOutputReceived(string line);
    Task OnExecutionCompleted(ExecutionRecord record);
    Task<UserDecision> RequestUserDecision(string message, UserDecision[] options);
    Task<string> AskUserTextInput(string question);
    Task<(ConfirmationResult result, string? modification)> ConfirmWithUser(string title, string details);
    Task<string?> OnUserInterrupt();
    CancellationToken CreateInterruptToken();
    void ResetInterruptToken();
}
