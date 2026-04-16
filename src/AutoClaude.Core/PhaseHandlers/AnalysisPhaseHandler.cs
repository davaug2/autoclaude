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
        // Step 1: Ask Claude for questions about the objective
        var questionsResult = await ExecuteCliAsync(context,
            $"O usuario tem o seguinte objetivo:\n\n{context.Session.Objective}\n\n" +
            $"Caminho do projeto: {context.Session.TargetPath}\n\n" +
            "Voce tem duvidas sobre este objetivo? Se sim, liste as duvidas.\n" +
            "Retorne um JSON: {\"questions\": [\"pergunta 1\", \"pergunta 2\"]}.\n" +
            "Se nao tiver duvidas, retorne {\"questions\": []}",
            "Coletando duvidas sobre o objetivo...", ct);

        if (!questionsResult.cliResult.IsSuccess)
            return PhaseResult.Failed(questionsResult.cliResult.StandardError);

        // Step 2: Parse and ask each question to the user
        var answers = new Dictionary<string, string>();
        var questions = ParseQuestions(questionsResult.responseText);

        foreach (var question in questions)
        {
            var answer = await _notifier.AskUserTextInput(question);
            answers[question] = answer;
        }

        // Step 3: Elaborate the objective with Claude
        var answersText = answers.Count > 0
            ? "\n\nRespostas do usuario:\n" + string.Join("\n", answers.Select(a => $"P: {a.Key}\nR: {a.Value}"))
            : "";

        var elaborateResult = await ExecuteCliAsync(context,
            $"Com base no objetivo do usuario e nas respostas fornecidas, elabore uma especificacao tecnica detalhada.\n\n" +
            $"Objetivo: {context.Session.Objective}{answersText}\n\n" +
            $"Caminho do projeto: {context.Session.TargetPath}\n\n" +
            "Crie uma especificacao clara e detalhada do que precisa ser feito.",
            "Elaborando objetivo...", ct);

        if (!elaborateResult.cliResult.IsSuccess)
            return PhaseResult.Failed(elaborateResult.cliResult.StandardError);

        // Step 4: Confirm with user
        var confirmed = await _notifier.ConfirmWithUser(
            "Objetivo elaborado",
            elaborateResult.responseText);

        if (!confirmed)
            return PhaseResult.Failed("Objetivo rejeitado pelo usuario");

        // Save to context
        var contextDict = JsonSerializer.Deserialize<Dictionary<string, object>>(context.Session.ContextJson) ?? new();
        contextDict["analysis_result"] = elaborateResult.responseText;
        var newContext = JsonSerializer.Serialize(contextDict);
        await _sessionRepo.UpdateContextAsync(context.Session.Id, newContext);
        context.Session.ContextJson = newContext;

        return PhaseResult.Succeeded(elaborateResult.responseText);
    }

    private async Task<(CliResult cliResult, string responseText)> ExecuteCliAsync(
        PhaseContext context, string prompt, string statusMessage, CancellationToken ct)
    {
        var record = new ExecutionRecord
        {
            SessionId = context.Session.Id,
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

    private static List<string> ParseQuestions(string responseText)
    {
        try
        {
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("questions", out var questions)
                    && questions.ValueKind == JsonValueKind.Array)
                {
                    return questions.EnumerateArray()
                        .Select(q => q.GetString() ?? "")
                        .Where(q => !string.IsNullOrWhiteSpace(q))
                        .ToList();
                }
            }
        }
        catch (JsonException) { }

        return new List<string>();
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
