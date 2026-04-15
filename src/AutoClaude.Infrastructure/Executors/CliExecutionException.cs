namespace AutoClaude.Infrastructure.Executors;

public class CliExecutionException : Exception
{
    public bool IsTransient { get; }

    public CliExecutionException(string message, bool isTransient = false)
        : base(message)
    {
        IsTransient = isTransient;
    }

    public CliExecutionException(string message, Exception innerException, bool isTransient = false)
        : base(message, innerException)
    {
        IsTransient = isTransient;
    }
}
