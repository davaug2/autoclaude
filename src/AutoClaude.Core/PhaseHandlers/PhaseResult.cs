namespace AutoClaude.Core.PhaseHandlers;

public class PhaseResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }

    public static PhaseResult Succeeded(string output) => new() { Success = true, Output = output };
    public static PhaseResult Failed(string error) => new() { Success = false, ErrorMessage = error };
}
