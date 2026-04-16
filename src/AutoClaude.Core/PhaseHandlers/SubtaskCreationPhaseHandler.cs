using System.Text;
using System.Text.Json;
using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;

namespace AutoClaude.Core.PhaseHandlers;

public class SubtaskCreationPhaseHandler : IPhaseHandler
{
    private const int MaxValidationRetries = 3;

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

        var task = context.CurrentTask;
        var objective = context.Session.Objective ?? "";
        List<SubtaskItem> subtasks;
        string? previousFailures = null;

        for (var attempt = 1; attempt <= MaxValidationRetries; attempt++)
        {
            // Step 1: Generate subtasks
            var generatePrompt = BuildGeneratePrompt(task, previousFailures);
            var generateResult = await ExecuteCliAsync(context, generatePrompt,
                $"Criando subtarefas: {task.Title} (tentativa {attempt}/{MaxValidationRetries})", ct);

            if (!generateResult.cliResult.IsSuccess)
                return PhaseResult.Failed(generateResult.cliResult.StandardError);

            subtasks = ParseSubtasks(generateResult.responseText, task.Id, context.Session.Id);
            if (subtasks.Count == 0)
                return PhaseResult.Failed("Nenhuma subtarefa foi gerada");

            // Step 2: Validate subtasks against objective
            var validatePrompt = BuildValidatePrompt(task, subtasks, objective);
            var validateResult = await ExecuteCliAsync(context, validatePrompt,
                "Validando subtarefas contra o objetivo...", ct);

            if (!validateResult.cliResult.IsSuccess)
                return PhaseResult.Failed(validateResult.cliResult.StandardError);

            var (isValid, issues) = ParseValidation(validateResult.responseText);

            if (isValid)
            {
                // Step 3: Confirm with user
                var summary = BuildSubtasksSummary(task, subtasks);
                var (confirmation, modification) = await _notifier.ConfirmWithUser("Subtarefas geradas e validadas", summary);

                if (confirmation == Domain.Enums.ConfirmationResult.Reject)
                    return PhaseResult.Failed("Subtarefas rejeitadas pelo usuario");

                if (confirmation == Domain.Enums.ConfirmationResult.Modify)
                {
                    context.Memory.AddTemporary("Modificacao nas subtarefas", modification!);
                    previousFailures = $"O usuario pediu modificacoes: {modification}";
                    continue;
                }

                foreach (var subtask in subtasks)
                    await _subtaskRepo.InsertAsync(subtask);

                return PhaseResult.Succeeded($"Criadas {subtasks.Count} subtarefas para '{task.Title}'");
            }

            previousFailures = issues;
            await _notifier.OnCliOutputReceived($"Validacao falhou (tentativa {attempt}): {issues}");
        }

        return PhaseResult.Failed($"Subtarefas nao passaram na validacao apos {MaxValidationRetries} tentativas");
    }

    private async Task<(CliResult cliResult, string responseText)> ExecuteCliAsync(
        PhaseContext context, string prompt, string statusMessage, CancellationToken ct)
    {
        var record = new ExecutionRecord
        {
            SessionId = context.Session.Id,
            TaskId = context.CurrentTask?.Id,
            PhaseId = context.Phase.Id,
            PromptSent = prompt
        };
        record.MarkStarted();
        await _executionRepo.InsertAsync(record);
        await _notifier.OnExecutionStarted(statusMessage);

        var request = new CliRequest
        {
            Prompt = prompt,
            WorkingDirectory = context.Session.TargetPath,
            OutputCallback = line => _notifier.OnCliOutputReceived(line)
        };

        var result = await _cliExecutor.ExecuteAsync(request, ct);
        var responseText = ExtractResultFromJson(result.StandardOutput);

        if (result.IsSuccess)
            record.MarkSuccess(responseText, result.StandardOutput, result.ExitCode, result.DurationMs);
        else
            record.MarkFailure(result.StandardError, result.ExitCode, result.DurationMs);

        await _executionRepo.UpdateAsync(record);
        await _notifier.OnExecutionCompleted(record);

        return (result, responseText);
    }

    private static string BuildGeneratePrompt(TaskItem task, string? previousFailures)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Para a seguinte tarefa, crie subtarefas com prompts prontos para execucao via Claude Code CLI.");
        sb.AppendLine();
        sb.AppendLine($"Tarefa: {task.Title}");
        sb.AppendLine($"Descricao: {task.Description}");

        if (!string.IsNullOrEmpty(previousFailures))
        {
            sb.AppendLine();
            sb.AppendLine("ATENCAO: A tentativa anterior foi rejeitada na validacao. Corrija os problemas:");
            sb.AppendLine(previousFailures);
        }

        sb.AppendLine();
        sb.AppendLine("Retorne um JSON array: [{\"title\": \"titulo\", \"prompt\": \"prompt completo para execucao\"}]");
        return sb.ToString();
    }

    private static string BuildValidatePrompt(TaskItem task, List<SubtaskItem> subtasks, string objective)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Valide se as subtarefas abaixo estao corretas para atingir o objetivo da tarefa.");
        sb.AppendLine();
        sb.AppendLine($"Objetivo geral: {objective}");
        sb.AppendLine($"Tarefa: {task.Title}");
        sb.AppendLine($"Descricao: {task.Description}");
        sb.AppendLine();
        sb.AppendLine("Subtarefas:");
        foreach (var sub in subtasks)
            sb.AppendLine($"  {sub.Ordinal}. {sub.Title}");
        sb.AppendLine();
        sb.AppendLine("Retorne um JSON: {\"valid\": true/false, \"issues\": \"descricao dos problemas se houver\"}");
        return sb.ToString();
    }

    private static string BuildSubtasksSummary(TaskItem task, List<SubtaskItem> subtasks)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Tarefa: {task.Title}");
        sb.AppendLine();
        foreach (var sub in subtasks)
            sb.AppendLine($"  {sub.Ordinal}. {sub.Title}");
        return sb.ToString();
    }

    private static (bool isValid, string issues) ParseValidation(string responseText)
    {
        try
        {
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                using var doc = JsonDocument.Parse(json);
                var valid = doc.RootElement.TryGetProperty("valid", out var v) && v.GetBoolean();
                var issues = doc.RootElement.TryGetProperty("issues", out var i) ? i.GetString() ?? "" : "";
                return (valid, issues);
            }
        }
        catch (JsonException) { }

        return (true, "");
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
