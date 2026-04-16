namespace AutoClaude.Core.Domain.Models;

public class CliRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public string? WorkingDirectory { get; set; }
    public int TimeoutSeconds { get; set; } = 300;
    public List<string> AdditionalArgs { get; set; } = new();
    public Action<string>? OutputCallback { get; set; }
}
