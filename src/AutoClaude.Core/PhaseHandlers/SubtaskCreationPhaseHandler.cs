using System.Text;
using System.Text.Json;
using AutoClaude.Core.Domain;
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

        var task = context.CurrentTask;

        var generatePrompt = BuildGeneratePrompt(task, previousFailures: null);
        var generateResult = await ExecuteCliAsync(context, generatePrompt,
            $"Criando subtarefas: {task.Title}", ct, systemPromptAppend: SubtaskSchema);

        if (!generateResult.cliResult.IsSuccess)
            return PhaseResult.Failed(generateResult.cliResult.StandardError ?? $"exit code {generateResult.cliResult.ExitCode}");

        var subtasks = ParseSubtasks(generateResult.responseText, generateResult.cliResult.OutputJson, task.Id, context.Session.Id);
        if (subtasks.Count == 0)
            return PhaseResult.Failed("Nenhuma subtarefa foi gerada");

        foreach (var subtask in subtasks)
            await _subtaskRepo.InsertAsync(subtask);

        return PhaseResult.Succeeded($"Criadas {subtasks.Count} subtarefas para '{task.Title}'");
    }

    private async Task<(CliResult cliResult, string responseText)> ExecuteCliAsync(
        PhaseContext context, string prompt, string statusMessage, CancellationToken ct,
        string? systemPromptAppend = null)
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
        await _notifier.OnExecutionStarted(statusMessage, prompt);

        var request = new CliRequest
        {
            SessionId = context.Session.Id,
            Prompt = prompt,
            SystemPromptAppend = systemPromptAppend,
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
        var responseText = AgentResponse.ExtractResult(result.StandardOutput, result.OutputJson);

        if (result.IsSuccess)
            record.MarkSuccess(responseText, result.StandardOutput, result.ExitCode, result.DurationMs);
        else
            record.MarkFailure(result.StandardError, result.ExitCode, result.DurationMs);

        await _executionRepo.UpdateAsync(record);
        await _notifier.OnExecutionCompleted(record);

        return (result, responseText);
    }

    internal const string SubtaskSchema =
        "Schema JSON para o arquivo de saida desta fase:\n" +
        "Um JSON array com as subtarefas:\n" +
        "[{\"title\": \"titulo\", \"prompt\": \"prompt completo para execucao\", \"working_directory\": \"caminho absoluto\"}]\n\n" +
        "Regras:\n" +
        "- O campo working_directory eh OBRIGATORIO em todas as subtarefas.\n" +
        "- Defina sempre o caminho absoluto da pasta correta onde cada subtarefa deve ser executada.\n" +
        "- Cada prompt deve ser completo e autocontido para execucao via Claude Code CLI.";

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

        return sb.ToString();
    }


    private static List<SubtaskItem> ParseSubtasks(string responseText, string? jsonFileContent, Guid taskId, Guid sessionId)
    {
        var subtasks = new List<SubtaskItem>();

        foreach (var jsonArray in AgentResponse.ExtractJsonBlocks(responseText, jsonFileContent))
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonArray);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                var hasSubtaskShape = false;
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("title", out _) && element.TryGetProperty("prompt", out _))
                    {
                        hasSubtaskShape = true;
                        break;
                    }
                }
                if (!hasSubtaskShape) continue;

                var ordinal = subtasks.Count + 1;
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty("title", out _) || !element.TryGetProperty("prompt", out _))
                        continue;

                    subtasks.Add(new SubtaskItem
                    {
                        TaskId = taskId,
                        SessionId = sessionId,
                        Title = element.GetProperty("title").GetString() ?? $"Subtask {ordinal}",
                        Prompt = element.GetProperty("prompt").GetString() ?? "",
                        WorkingDirectory = element.TryGetProperty("working_directory", out var wd) ? wd.GetString() : null,
                        Ordinal = ordinal++
                    });
                }

                if (subtasks.Count > 0) break; // Found the subtasks array, stop looking
            }
            catch (JsonException) { }
        }

        return subtasks;
    }


}
