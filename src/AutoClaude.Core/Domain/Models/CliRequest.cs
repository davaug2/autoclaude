namespace AutoClaude.Core.Domain.Models;

public class CliRequest
{
    public Guid? SessionId { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public string? WorkingDirectory { get; set; }
    public int TimeoutSeconds { get; set; } = 120; // kept for backward compat
    public int IdleTimeoutSeconds { get; set; } = 120;
    public int? MaxTurns { get; set; }
    public List<string> AdditionalArgs { get; set; } = new();
    public List<string> AllowedDirectories { get; set; } = new();
    public bool AllowWrite { get; set; }
    public Func<string, Task>? OutputCallback { get; set; }
    public string? ResumeSessionId { get; set; }
    public Func<int, TimeSpan, string?, Task>? RetryCallback { get; set; }
    public Func<int, Task>? RetryExecutingCallback { get; set; }

    /// <summary>
    /// Path to a file where Claude will write its structured JSON output.
    /// If null, the executor generates a unique temp file path automatically.
    /// The directory containing this file is auto-added to allowed directories
    /// so Claude has permission to write it via the Write tool.
    /// </summary>
    public string? OutputJsonFilePath { get; set; }

    /// <summary>
    /// Extra text appended to the system prompt (after the base system prompt and before the
    /// output-file instruction). Use this to add output schema or phase-specific instructions
    /// without polluting the user-facing prompt.
    /// </summary>
    public string? SystemPromptAppend { get; set; }
}
