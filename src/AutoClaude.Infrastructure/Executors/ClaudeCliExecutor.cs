using System.Diagnostics;
using System.Text;
using AutoClaude.Core.Domain.Models;
using AutoClaude.Core.Ports;
using Polly;
using Polly.CircuitBreaker;

namespace AutoClaude.Infrastructure.Executors;

public class ClaudeCliExecutor : ICliExecutor
{
    private readonly IAsyncPolicy<CliResult> _resiliencePolicy;

    public string CliType => "claude";

    public ClaudeCliExecutor()
    {
        var retryPolicy = Policy<CliResult>
            .Handle<CliExecutionException>(ex => ex.IsTransient)
            .OrResult(r => !r.IsSuccess && IsTransient(r))
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

        var circuitBreaker = Policy<CliResult>
            .Handle<CliExecutionException>()
            .OrResult(r => !r.IsSuccess)
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

        _resiliencePolicy = Policy.WrapAsync(retryPolicy, circuitBreaker);
    }

    public async Task<CliResult> ExecuteAsync(CliRequest request, CancellationToken ct = default)
    {
        try
        {
            return await _resiliencePolicy.ExecuteAsync(async (cancellationToken) =>
            {
                return await ExecuteProcessAsync(request, cancellationToken);
            }, ct);
        }
        catch (BrokenCircuitException)
        {
            return new CliResult
            {
                ExitCode = -2,
                StandardError = "Circuit breaker is open. Too many consecutive failures.",
                DurationMs = 0
            };
        }
    }

    private async Task<CliResult> ExecuteProcessAsync(CliRequest request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var args = BuildArguments(request);

        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = request.WorkingDirectory ?? Directory.GetCurrentDirectory()
        };

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            sw.Stop();
            throw new CliExecutionException($"Failed to start claude CLI: {ex.Message}", ex, isTransient: false);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            await process.WaitForExitAsync(timeoutCts.Token);

            sw.Stop();
            return new CliResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = await stdoutTask,
                StandardError = await stderrTask,
                DurationMs = sw.ElapsedMilliseconds
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
                    ? "Operation was cancelled"
                    : "Process timed out",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    internal static string BuildArguments(CliRequest request)
    {
        var sb = new StringBuilder();
        sb.Append("--print --output-format json");

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            sb.Append(" --system-prompt ");
            sb.Append(EscapeArgument(request.SystemPrompt));
        }

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

    private static bool IsTransient(CliResult result)
    {
        return result.ExitCode == -1
            || (result.StandardError?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ?? false)
            || (result.StandardError?.Contains("connection", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
