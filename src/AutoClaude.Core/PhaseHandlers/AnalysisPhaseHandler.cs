using System.Text.Json;
using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;

namespace AutoClaude.Core.PhaseHandlers;

public class AnalysisPhaseHandler : IPhaseHandler
{
    private readonly ICliExecutor _cliExecutor;
    private readonly ISessionRepository _sessionRepo;
    private readonly IExecutionRecordRepository _executionRepo;
    private readonly IOrchestrationNotifier _notifier;

    public PhaseType HandledPhase => PhaseType.Analysis;

    public AnalysisPhaseHandler(
        ICliExecutor cliExecutor,
        ISessionRepository sessionRepo,
        IExecutionRecordRepository executionRepo,
        IOrchestrationNotifier notifier)
    {
        _cliExecutor = cliExecutor;
        _sessionRepo = sessionRepo;
        _executionRepo = executionRepo;
        _notifier = notifier;
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

        await _notifier.OnExecutionStarted($"Análise: {context.Session.Objective}");

        var request = new CliRequest
        {
            Prompt = prompt,
            SystemPrompt = context.Phase.SystemPrompt,
            WorkingDirectory = context.Session.TargetPath,
            OutputCallback = line => _notifier.OnCliOutputReceived(line)
        };

        var result = await _cliExecutor.ExecuteAsync(request, ct);

        if (result.IsSuccess)
        {
            var responseText = ExtractResultFromJson(result.StandardOutput);
            record.MarkSuccess(responseText, result.StandardOutput, result.ExitCode, result.DurationMs);
            await _executionRepo.UpdateAsync(record);
            await _notifier.OnExecutionCompleted(record);

            var contextDict = JsonSerializer.Deserialize<Dictionary<string, object>>(context.Session.ContextJson) ?? new();
            contextDict["analysis_result"] = responseText;
            var newContext = JsonSerializer.Serialize(contextDict);
            await _sessionRepo.UpdateContextAsync(context.Session.Id, newContext);
            context.Session.ContextJson = newContext;

            return PhaseResult.Succeeded(responseText);
        }

        record.MarkFailure(result.StandardError, result.ExitCode, result.DurationMs);
        await _executionRepo.UpdateAsync(record);
        await _notifier.OnExecutionCompleted(record);
        return PhaseResult.Failed(result.StandardError);
    }

    private string BuildPrompt(PhaseContext context)
    {
        if (!string.IsNullOrEmpty(context.Phase.PromptTemplate))
        {
            return context.Phase.PromptTemplate
                .Replace("{{objective}}", context.Session.Objective ?? "")
                .Replace("{{target_path}}", context.Session.TargetPath ?? "");
        }

        return $"Analyze the following objective and generate a detailed technical specification.\n\nObjective: {context.Session.Objective}";
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
