using System.Text.Json;
using AutoClaude.Core.Domain;
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
        // Step 1-2: Loop de perguntas — Claude pergunta, usuário responde, repete até não ter mais dúvidas
        var round = 1;
        var skipElaboration = false;
        string currentSpec = "";
        while (true)
        {
            var memoryText = context.Memory.ToPromptText();
            var roundLabel = round == 1 ? "Coletando duvidas sobre o objetivo..." : $"Verificando duvidas adicionais (rodada {round})...";

            var firstRoundInstructions = round == 1
                ? "ANTES de formular suas duvidas:\n" +
                  "1. Analise a arquitetura dos projetos nos diretorios informados que seja relevante ao objetivo.\n" +
                  "2. Identifique stack, padroes de codigo, dependencias e estrutura do projeto.\n" +
                  "3. Determine o DIRETORIO DE TRABALHO principal para esta tarefa. Se o objetivo mencionar caminhos em projetos diferentes, " +
                  "escolha o diretorio raiz mais adequado (ex: se menciona C:\\projetos\\helena_api e C:\\projetos\\helena_web, use C:\\projetos). " +
                  "Se menciona apenas um projeto, use a pasta desse projeto. Inclua como memory com text='working_directory' e answer='caminho absoluto'.\n" +
                  "4. Inclua essas descobertas no JSON de resposta como itens com answer preenchida (o usuario nao precisa responder essas).\n" +
                  "5. Depois, inclua suas duvidas reais para o usuario (com answer vazia).\n\n"
                : "Com base nas respostas anteriores, voce tem mais alguma duvida?\n" +
                  "Se descobriu algo novo sobre o projeto, inclua tambem como item com answer preenchida.\n\n";

            const string analysisSchema =
                "Schema JSON para o arquivo de saida desta fase:\n" +
                "{\n" +
                "  \"memories\": [\n" +
                "    { \"text\": \"titulo da descoberta\", \"answer\": \"o que voce descobriu\", \"memory\": \"persistent\" }\n" +
                "  ],\n" +
                "  \"questions\": [\n" +
                "    { \"text\": \"sua duvida para o usuario\", \"memory\": \"persistent\" }\n" +
                "  ]\n" +
                "}\n" +
                "- memories: informacoes que VOCE descobriu analisando o codigo (salvas automaticamente).\n" +
                "- questions: duvidas que VOCE tem para o usuario responder.\n" +
                "- Use memory=persistent para informacoes validas para toda a sessao.\n" +
                "- Use memory=temporary para informacoes especificas desta fase.\n" +
                "- Se nao tiver duvidas nem descobertas, retorne memories e questions como arrays vazios.";

            var questionsResult = await ExecuteCliAsync(context,
                $"O usuario tem o seguinte objetivo:\n\n{context.Session.Objective}\n\n" +
                memoryText + "\n\n" +
                firstRoundInstructions.TrimEnd(),
                roundLabel, ct, systemPromptAppend: analysisSchema);

            if (!questionsResult.cliResult.IsSuccess)
            {
                var stderr = questionsResult.cliResult.StandardError;
                var code = questionsResult.cliResult.ExitCode;
                var detail = string.IsNullOrWhiteSpace(stderr)
                    ? $"CLI retornou exit code {code} sem mensagem de erro"
                    : stderr;
                return PhaseResult.Failed(detail);
            }

            var agentResponse = questionsResult.parsed;

            // Auto-save agent discoveries
            foreach (var m in agentResponse.Memories)
            {
                context.Memory.Add(m.Text, m.Answer, m.Persistent);

                // If the agent determined the working directory, update session's TargetPath
                if (m.Text.Equals("working_directory", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(m.Answer)
                    && Directory.Exists(m.Answer))
                {
                    context.Session.TargetPath = m.Answer;
                    await _sessionRepo.UpdateTargetPathAsync(context.Session.Id, m.Answer);
                }
            }
            if (agentResponse.Memories.Count > 0)
                await context.SaveMemoryAsync();

            if (agentResponse.Questions.Count == 0)
            {
                // If the agent already provided a result (spec) alongside memories,
                // skip the separate elaboration step — it's already done.
                if (!string.IsNullOrWhiteSpace(agentResponse.Result))
                {
                    currentSpec = agentResponse.Result;
                    skipElaboration = true;
                }
                break;
            }

            // Ask user each question
            foreach (var q in agentResponse.Questions)
            {
                var answer = await _notifier.AskUserTextInput(q.Text);
                context.Memory.Add(q.Text, answer, q.Persistent);
                await context.SaveMemoryAsync();
            }

            round++;
        }

        // Step 3: Elaborate the objective with Claude (skip if already provided in analysis)
        if (!skipElaboration)
        {
            const string elaborateSchema =
                "Schema JSON para o arquivo de saida:\n" +
                "{\"result\": \"a especificacao tecnica completa\"}";

            var elaborateResult = await ExecuteCliAsync(context,
                $"Com base no objetivo do usuario e nas respostas fornecidas, elabore uma especificacao tecnica detalhada.\n\n" +
                $"Objetivo: {context.Session.Objective}\n\n" +
                $"Caminho do projeto: {context.Session.TargetPath}\n" +
                context.Memory.ToPromptText() + "\n\n" +
                "Crie uma especificacao clara e detalhada do que precisa ser feito.",
                "Elaborando objetivo...", ct, systemPromptAppend: elaborateSchema);

            if (!elaborateResult.cliResult.IsSuccess)
            {
                var stderr = elaborateResult.cliResult.StandardError;
                var code = elaborateResult.cliResult.ExitCode;
                var detail = string.IsNullOrWhiteSpace(stderr)
                    ? $"CLI retornou exit code {code} sem mensagem de erro"
                    : stderr;
                return PhaseResult.Failed(detail);
            }

            currentSpec = elaborateResult.responseText ?? "";
        }
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
            await context.SaveMemoryAsync();
            var modifyResult = await ExecuteCliAsync(context,
                $"O usuario pediu modificacoes na especificacao.\n\n" +
                $"Especificacao atual:\n{currentSpec}\n\n" +
                $"Modificacao solicitada: {modification}\n" +
                context.Memory.ToPromptText() + "\n\n" +
                "Reelabore a especificacao com as modificacoes.",
                "Reelaborando objetivo...", ct);

            if (!modifyResult.cliResult.IsSuccess)
                return PhaseResult.Failed(modifyResult.cliResult.StandardError ?? $"exit code {modifyResult.cliResult.ExitCode}");

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

    private async Task<(CliResult cliResult, AgentResponse parsed, string responseText)> ExecuteCliAsync(
        PhaseContext context, string prompt, string statusMessage, CancellationToken ct,
        string? systemPromptAppend = null)
    {
        var record = new ExecutionRecord
        {
            SessionId = context.Session.Id,
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
            WorkingDirectory = context.Session.TargetPath,
            AllowedDirectories = context.Session.AllowedDirectories,
            AllowWrite = context.AllowWrite,
            ResumeSessionId = context.CliSessionId,
            SystemPromptAppend = systemPromptAppend,
            OutputCallback = async line => await _notifier.OnCliOutputReceived(line),
            RetryCallback = async (attempt, delay, reason) =>
                await _notifier.OnRetryStarted(attempt, delay, reason),
            RetryExecutingCallback = async (attempt) =>
                await _notifier.OnRetryExecuting(attempt)
        };

        var result = await _cliExecutor.ExecuteAsync(request, ct);

        // Capture CLI session ID for resume
        if (!string.IsNullOrEmpty(result.CliSessionId))
            context.CliSessionId = result.CliSessionId;

        var parsed = AgentResponse.Parse(result.StandardOutput, result.OutputJson);
        var responseText = parsed.Result ?? parsed.Narrative;

        if (result.IsSuccess)
        {
            record.MarkSuccess(responseText, result.StandardOutput, result.ExitCode, result.DurationMs);
        }
        else
        {
            var debugInfo = $"ExitCode={result.ExitCode}";
            if (!string.IsNullOrWhiteSpace(result.StandardError))
                debugInfo += $" | StdErr: {result.StandardError[..Math.Min(result.StandardError.Length, 300)]}";
            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                debugInfo += $" | StdOut: {result.StandardOutput[..Math.Min(result.StandardOutput.Length, 300)]}";
            else
                debugInfo += " | StdOut: (vazio)";

            record.MarkFailure(debugInfo, result.ExitCode, result.DurationMs);
        }

        await _executionRepo.UpdateAsync(record);
        await _notifier.OnExecutionCompleted(record);

        return (result, parsed, responseText);
    }
}
