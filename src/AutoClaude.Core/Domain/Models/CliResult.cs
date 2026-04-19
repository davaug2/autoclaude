namespace AutoClaude.Core.Domain.Models;

public class CliResult
{
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public string? CliSessionId { get; set; }
    public bool IsSuccess => ExitCode == 0;

    /// <summary>
    /// Raw JSON content read from the output file (when OutputJsonFilePath was used).
    /// Null if the file was not written or could not be read.
    /// Phase handlers should prefer this over parsing JSON from StandardOutput.
    /// </summary>
    public string? OutputJson { get; set; }
}
