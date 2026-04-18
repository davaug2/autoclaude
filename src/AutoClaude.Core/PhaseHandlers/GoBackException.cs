namespace AutoClaude.Core.PhaseHandlers;

public class GoBackException : Exception
{
    public string? TargetPhase { get; }

    public GoBackException() : base("User requested to go back to previous phase") { }

    public GoBackException(string? targetPhase)
        : base($"User requested to go back to phase: {targetPhase ?? "previous"}")
    {
        TargetPhase = targetPhase;
    }
}
