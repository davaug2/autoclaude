namespace AutoClaude.Core.Ports;

public interface IAutoClaudeAppSettings
{
    string SettingsFilePath { get; }
    bool DebugClaudeCommands { get; }
    void Reload();
    void SetDebugClaudeCommands(bool value);
    void Save();
}
