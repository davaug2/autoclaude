namespace AutoClaude.Core.Domain.Models;

public class CliRequest
{
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
}
