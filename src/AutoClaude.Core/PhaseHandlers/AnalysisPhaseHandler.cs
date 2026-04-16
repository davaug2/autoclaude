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
        var memoryText = context.Memory.ToPromptText();

        // Step 1: Ask Claude for questions about the objective
        var questionsResult = await ExecuteCliAsync(context,
            $"O usuario tem o seguinte objetivo:\n\n{context.Session.Objective}\n\n" +
            $"Caminho do projeto: {context.Session.TargetPath}\n" +
            memoryText + "\n\n" +
            "Voce tem duvidas sobre este objetivo? Se sim, liste as duvidas.\n" +
            "Para cada duvida, indique se a resposta deve ser memorizada para toda a sessao (persistent) ou apenas para esta fase (temporary).\n" +
            "Retorne um JSON: {\"questions\": [{\"text\": \"pergunta\", \"memory\": \"persistent|temporary\"}]}.\n" +
            "Se nao tiver duvidas, retorne {\"questions\": []}",
            "Coletando duvidas sobre o objetivo...", ct);

        if (!questionsResult.cliResult.IsSuccess)
            return PhaseResult.Failed(questionsResult.cliResult.StandardError);

        // Step 2: Parse and ask each question to the user, save to memory
        var questions = ParseQuestions(questionsResult.responseText);

        foreach (var q in questions)
        {
            var answer = await _notifier.AskUserTextInput(q.Text);
            context.Memory.Add(q.Text, answer, q.Persistent);
        }

        // Step 3: Elaborate the objective with Claude
        var elaborateResult = await ExecuteCliAsync(context,
            $"Com base no objetivo do usuario e nas respostas fornecidas, elabore uma especificacao tecnica detalhada.\n\n" +
            $"Objetivo: {context.Session.Objective}\n\n" +
            $"Caminho do projeto: {context.Session.TargetPath}\n" +
            context.Memory.ToPromptText() + "\n\n" +
            "Crie uma especificacao clara e detalhada do que precisa ser feito.",
            "Elaborando objetivo...", ct);

        if (!elaborateResult.cliResult.IsSuccess)
            return PhaseResult.Failed(elaborateResult.cliResult.StandardError);

        // Step 4: Confirm with user (loop for modifications)
        var currentSpec = elaborateResult.responseText;
        while (true)
        {
            var (confirmation, modification) = await _notifier.ConfirmWithUser("Objetivo elaborado", currentSpec);

            if (confirmation == Domain.Enums.ConfirmationResult.Reject)
                return PhaseResult.Failed("Objetivo rejeitado pelo usuario");

            if (confirmation == Domain.Enums.ConfirmationResult.GoBack)
                throw new GoBackException();

            if (confirmation == Domain.Enums.ConfirmationResult.Confirm)
                break;

            // Modify: re-elaborate with user instruction
            context.Memory.AddTemporary("Modificacao solicitada", modification!);
            var modifyResult = await ExecuteCliAsync(context,
                $"O usuario pediu modificacoes na especificacao.\n\n" +
                $"Especificacao atual:\n{currentSpec}\n\n" +
                $"Modificacao solicitada: {modification}\n" +
                context.Memory.ToPromptText() + "\n\n" +
                "Reelabore a especificacao com as modificacoes.",
                "Reelaborando objetivo...", ct);

            if (!modifyResult.cliResult.IsSuccess)
                return PhaseResult.Failed(modifyResult.cliResult.StandardError);

            currentSpec = modifyResult.responseText;
        }

        // Save to context
        var contextDict = JsonSerializer.Deserialize<Dictionary<string, object>>(context.Session.ContextJson) ?? new();
        contextDict["analysis_result"] = currentSpec;
        var newContext = JsonSerializer.Serialize(contextDict);
        await _sessionRepo.UpdateContextAsync(context.Session.Id, newContext);
        context.Session.ContextJson = newContext;

        return PhaseResult.Succeeded(currentSpec);
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
            AllowedDirectories = context.Session.AllowedDirectories,
            AllowWrite = context.AllowWrite,
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

    private record ParsedQuestion(string Text, bool Persistent);

    private static List<ParsedQuestion> ParseQuestions(string responseText)
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
                    var result = new List<ParsedQuestion>();
                    foreach (var q in questions.EnumerateArray())
                    {
                        if (q.ValueKind == JsonValueKind.Object)
                        {
                            var text = q.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                            var mem = q.TryGetProperty("memory", out var m) ? m.GetString() ?? "temporary" : "temporary";
                            if (!string.IsNullOrWhiteSpace(text))
                                result.Add(new ParsedQuestion(text, mem == "persistent"));
                        }
                        else if (q.ValueKind == JsonValueKind.String)
                        {
                            var text = q.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(text))
                                result.Add(new ParsedQuestion(text, false));
                        }
                    }
                    return result;
                }
            }
        }
        catch (JsonException) { }

        return new List<ParsedQuestion>();
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
