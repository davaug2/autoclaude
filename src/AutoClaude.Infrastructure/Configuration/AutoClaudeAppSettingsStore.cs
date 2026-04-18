using System.Text.Json;
using AutoClaude.Core.Ports;

namespace AutoClaude.Infrastructure.Configuration;

public sealed class AutoClaudeAppSettingsStore : IAutoClaudeAppSettings
{
    private readonly string _path;
    private readonly object _lock = new();
    private bool _debugClaudeCommands;

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AutoClaudeAppSettingsStore(string path)
    {
        _path = path;
        Reload();
    }

    public string SettingsFilePath => _path;

    public bool DebugClaudeCommands
    {
        get
        {
            lock (_lock)
                return _debugClaudeCommands;
        }
    }

    public void Reload()
    {
        lock (_lock)
        {
            _debugClaudeCommands = false;
            if (!File.Exists(_path))
                return;
            try
            {
                var json = File.ReadAllText(_path);
                var dto = JsonSerializer.Deserialize<AutoClaudeSettingsDto>(json, JsonReadOptions);
                if (dto != null)
                    _debugClaudeCommands = dto.DebugClaudeCommands;
            }
            catch
            {
            }
        }
    }

    public void SetDebugClaudeCommands(bool value)
    {
        lock (_lock)
            _debugClaudeCommands = value;
    }

    public void Save()
    {
        lock (_lock)
        {
            var dto = new AutoClaudeSettingsDto { DebugClaudeCommands = _debugClaudeCommands };
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(dto, JsonWriteOptions);
            File.WriteAllText(_path, json);
        }
    }

    private sealed class AutoClaudeSettingsDto
    {
        public bool DebugClaudeCommands { get; set; }
    }
}
