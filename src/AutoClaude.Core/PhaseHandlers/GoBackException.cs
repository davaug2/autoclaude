namespace AutoClaude.Core.PhaseHandlers;

public class GoBackException : Exception
{
    public GoBackException() : base("User requested to go back to previous phase") { }
}
