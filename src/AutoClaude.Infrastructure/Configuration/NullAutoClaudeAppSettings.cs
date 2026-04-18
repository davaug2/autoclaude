using AutoClaude.Core.Ports;

namespace AutoClaude.Infrastructure.Configuration;

public sealed class NullAutoClaudeAppSettings : IAutoClaudeAppSettings
{
    public string SettingsFilePath => "";
    public bool DebugClaudeCommands => false;
    public void Reload() { }
    public void SetDebugClaudeCommands(bool value) { }
    public void Save() { }
}
