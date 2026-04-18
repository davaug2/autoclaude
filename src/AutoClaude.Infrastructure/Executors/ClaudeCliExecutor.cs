using System.Diagnostics;
using System.Text;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using Polly;
using Polly.CircuitBreaker;

namespace AutoClaude.Infrastructure.Executors;

public class ClaudeCliExecutor : ICliExecutor
{
    private readonly IAutoClaudeAppSettings _appSettings;
    private readonly IAsyncPolicy<CliResult> _circuitBreaker;

    public string CliType => "claude";

    public ClaudeCliExecutor(IAutoClaudeAppSettings appSettings)
    {
        _appSettings = appSettings;
        _circuitBreaker = Policy<CliResult>
            .Handle<CliExecutionException>()
            .OrResult(r => !r.IsSuccess)
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
    }

    public async Task<CliResult> ExecuteAsync(CliRequest request, CancellationToken ct = default)
    {
        var retryPolicy = Policy<CliResult>
            .Handle<CliExecutionException>(ex => ex.IsTransient)
            .OrResult(r => !r.IsSuccess && IsTransient(r))
            .WaitAndRetryAsync(3,
                attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetryAsync: async (outcome, delay, attemptNumber, ctx) =>
                {
                    var reason = outcome.Exception?.Message
                        ?? outcome.Result?.StandardError
                        ?? (outcome.Result is { ExitCode: var code } ? $"exit code {code}" : "erro desconhecido");
                    if (request.RetryCallback != null)
                        await request.RetryCallback(attemptNumber, delay, reason);
                });

        var resiliencePolicy = Policy.WrapAsync(retryPolicy, _circuitBreaker);

        int attemptNumber = 0;
        try
        {
            return await resiliencePolicy.ExecuteAsync(async (cancellationToken) =>
            {
                attemptNumber++;
                if (attemptNumber > 1 && request.RetryExecutingCallback != null)
                    await request.RetryExecutingCallback(attemptNumber - 1);
                cancellationToken.ThrowIfCancellationRequested();
                return await ExecuteProcessAsync(request, cancellationToken);
            }, ct);
        }
        catch (BrokenCircuitException)
        {
            return new CliResult
            {
                ExitCode = -2,
                StandardError = "Circuit breaker aberto. Muitas falhas consecutivas.",
                DurationMs = 0
            };
        }
    }

    private async Task<CliResult> ExecuteProcessAsync(CliRequest request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var args = BuildArguments(request);
        var workingDirectory = request.WorkingDirectory ?? Directory.GetCurrentDirectory();

        _appSettings.Reload();
        if (_appSettings.DebugClaudeCommands)
        {
            try { ClaudeDebugConsole.WriteCommandPanel(workingDirectory, args); }
            catch { /* No console available (e.g. WinUI app) */ }
        }

        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            sw.Stop();
            throw new CliExecutionException($"Falha ao iniciar claude CLI: {ex.Message}", ex, isTransient: false);
        }

        // Idle timeout: cancels if no output received for IdleTimeoutSeconds (default 120s).
        // Resets every time a line is read, so long-running active processes won't be killed.
        using var idleCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, idleCts.Token);
        void ResetIdleTimeout() => idleCts.CancelAfter(TimeSpan.FromSeconds(request.IdleTimeoutSeconds));
        ResetIdleTimeout();

        var allLines = new StringBuilder();
        string? resultJson = null;
        string? extractedSessionId = null;
        var displayBuffer = new StringBuilder();

        try
        {
            var stderrTask = ReadStderrSafeAsync(process, linkedCts.Token);

            var readTask = Task.Run(async () =>
            {
                try
                {
                    while (!process.StandardOutput.EndOfStream)
                    {
                        linkedCts.Token.ThrowIfCancellationRequested();
                        var line = await process.StandardOutput.ReadLineAsync(linkedCts.Token);
                        if (line == null) continue;

                        ResetIdleTimeout();
                        allLines.AppendLine(line);

                        var (text, kind) = ExtractDisplayText(line);
                        if (text != null)
                        {
                            if (kind == OutputKind.Delta)
                            {
                                // Delta: append to buffer and send to display
                                displayBuffer.Append(text);
                                if (request.OutputCallback != null)
                                    await request.OutputCallback(text);
                            }
                            else
                            {
                                // Full message: replace buffer (don't send to display — deltas already showed it)
                                displayBuffer.Clear();
                                displayBuffer.Append(text);
                            }
                        }

                        if (IsResultLine(line))
                        {
                            resultJson = line;
                            extractedSessionId = ExtractSessionId(line);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
                {
                    /* Process exited — handle became invalid */
                }
            }, linkedCts.Token);

            try
            {
                await Task.WhenAll(readTask, process.WaitForExitAsync(linkedCts.Token));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Process exited unexpectedly or handle became invalid — continue to read output
            }

            sw.Stop();
            var exitCode = GetExitCodeSafe(process);
            return new CliResult
            {
                ExitCode = exitCode,
                StandardOutput = resultJson ?? allLines.ToString(),
                StandardError = await stderrTask,
                DurationMs = sw.ElapsedMilliseconds,
                CliSessionId = extractedSessionId
            };
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }

            sw.Stop();
            return new CliResult
            {
                ExitCode = -1,
                StandardError = ct.IsCancellationRequested
                    ? "Operacao cancelada"
                    : $"Processo inativo por {request.IdleTimeoutSeconds}s sem saida",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Handle invalid handle errors when process exits unexpectedly
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }

            sw.Stop();
            var exitCode = GetExitCodeSafe(process);
            var output = resultJson ?? allLines.ToString();
            var hasOutput = !string.IsNullOrWhiteSpace(output);

            return new CliResult
            {
                ExitCode = hasOutput ? exitCode : -1,
                StandardOutput = output,
                StandardError = hasOutput ? "" : $"Processo encerrado inesperadamente: {ex.GetType().Name}: {ex.Message}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    private static int GetExitCodeSafe(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static async Task<string> ReadStderrSafeAsync(Process process, CancellationToken ct)
    {
        try
        {
            return await process.StandardError.ReadToEndAsync(ct);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return "";
        }
    }

    internal static string BuildArguments(CliRequest request)
    {
        var sb = new StringBuilder();
        sb.Append("--print --output-format stream-json --include-partial-messages --permission-mode auto");

        if (!string.IsNullOrEmpty(request.ResumeSessionId))
        {
            sb.Append(" --resume ");
            sb.Append(EscapeArgument(request.ResumeSessionId));
        }

        if (!request.AllowWrite)
            sb.Append(" --disallowedTools \"Edit Write NotebookEdit\"");

        if (request.MaxTurns.HasValue)
            sb.Append($" --max-turns {request.MaxTurns.Value}");

        foreach (var dir in request.AllowedDirectories)
        {
            sb.Append(" --add-dir ");
            sb.Append(EscapeArgument(dir));
        }

        var systemPrompt = request.SystemPrompt;
        if (string.IsNullOrEmpty(systemPrompt))
        {
            systemPrompt = "Responda sempre em portugues brasileiro.\n\n" +
                "IMPORTANTE: Narre cada acao que voce esta realizando em tempo real, como um log de progresso. " +
                "Antes de usar qualquer ferramenta, escreva uma linha curta descrevendo o que vai fazer. Exemplos:\n" +
                "- 'Analisando estrutura do projeto...'\n" +
                "- 'Lendo arquivo src/Controllers/AuthController.cs...'\n" +
                "- 'Buscando referencias de IAccessLogService...'\n" +
                "- 'Verificando dependencias no .csproj...'\n" +
                "Isso permite que o usuario acompanhe o progresso.\n\n" +
                "Seja detalhado nas conclusoes e explique seu raciocinio.\n\n" +
                "Sempre que descobrir informacoes relevantes sobre o projeto (arquitetura, padroes, stack, " +
                "dependencias, convencoes de codigo), inclua no JSON de resposta.\n\n" +
                "FORMATO DE RESPOSTA: Escreva texto livre narrando suas acoes. " +
                "Ao final, inclua um bloco ```json com dados estruturados no seguinte formato:\n" +
                "```json\n" +
                "{\n" +
                "  \"memories\": [{\"text\": \"titulo\", \"answer\": \"descoberta\", \"memory\": \"persistent|temporary\"}],\n" +
                "  \"questions\": [{\"text\": \"duvida para o usuario\", \"memory\": \"persistent|temporary\"}],\n" +
                "  \"result\": \"texto principal da resposta (opcional)\"\n" +
                "}\n" +
                "```\n" +
                "- memories: informacoes que VOCE descobriu (salvas automaticamente, sem perguntar ao usuario).\n" +
                "- questions: duvidas para o usuario responder.\n" +
                "- result: resposta principal (especificacao, analise, etc).\n" +
                "- Todos os campos sao opcionais. Use apenas os que fizerem sentido para a tarefa.";
        }
        sb.Append(" --system-prompt ");
        sb.Append(EscapeArgument(systemPrompt));

        foreach (var arg in request.AdditionalArgs)
        {
            sb.Append(' ');
            sb.Append(arg);
        }

        sb.Append(" -p ");
        sb.Append(EscapeArgument(request.Prompt));

        return sb.ToString();
    }

    private static string EscapeArgument(string arg)
    {
        return $"\"{arg.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private enum OutputKind { Delta, Full }

    private static (string? text, OutputKind kind) ExtractDisplayText(string jsonLine)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
                return (null, OutputKind.Delta);

            var type = typeProp.GetString();

            // Stream delta — partial token
            if (type == "stream_event" && root.TryGetProperty("event", out var evt))
            {
                if (evt.TryGetProperty("type", out var evtType))
                {
                    var evtTypeStr = evtType.GetString();

                    // New text block starting — add line break to separate from previous block
                    if (evtTypeStr == "content_block_start"
                        && evt.TryGetProperty("content_block", out var cb)
                        && cb.TryGetProperty("type", out var cbType)
                        && cbType.GetString() == "text")
                    {
                        return ("\n", OutputKind.Delta);
                    }

                    if (evtTypeStr == "content_block_delta"
                        && evt.TryGetProperty("delta", out var delta)
                        && delta.TryGetProperty("text", out var deltaText))
                    {
                        return (deltaText.GetString(), OutputKind.Delta);
                    }
                }
            }

            // Assistant message — full accumulated text
            if (type == "assistant" && root.TryGetProperty("message", out var msg)
                && msg.TryGetProperty("content", out var content)
                && content.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text"
                        && block.TryGetProperty("text", out var textProp))
                    {
                        return (textProp.GetString(), OutputKind.Full);
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException) { }

        return (null, OutputKind.Delta);
    }

    private static string? ExtractSessionId(string jsonLine)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonLine);
            if (doc.RootElement.TryGetProperty("session_id", out var sid))
                return sid.GetString();
        }
        catch (System.Text.Json.JsonException) { }
        return null;
    }

    private static bool IsResultLine(string jsonLine)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonLine);
            return doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "result";
        }
        catch (System.Text.Json.JsonException) { return false; }
    }

    private static bool IsTransient(CliResult result)
    {
        if (result.StandardError?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true
            || result.StandardError?.Contains("canceled", StringComparison.OrdinalIgnoreCase) == true)
            return false;

        return result.ExitCode == -1
            || (result.StandardError?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ?? false)
            || (result.StandardError?.Contains("connection", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
