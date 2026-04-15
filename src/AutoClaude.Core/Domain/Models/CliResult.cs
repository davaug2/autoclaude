namespace AutoClaude.Core.Domain.Models;

public class CliResult
{
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public bool IsSuccess => ExitCode == 0;
}
