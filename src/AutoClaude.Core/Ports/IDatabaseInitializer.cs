namespace AutoClaude.Core.Ports;

public interface IDatabaseInitializer
{
    Task InitializeAsync();
}
