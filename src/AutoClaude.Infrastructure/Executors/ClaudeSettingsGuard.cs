namespace AutoClaude.Infrastructure.Executors;

/// <summary>
/// Installs a temporary .claude/settings.local.json that grants Claude Code
/// full permissions for the duration of a CLI execution.
/// If the file already exists it is backed up and restored afterwards.
///
/// Safety: the backup is a COPY of the original. If Restore() never runs
/// (crash, kill), the next Install() detects the leftover backup and
/// restores it before proceeding. The original is never lost.
/// </summary>
internal sealed class ClaudeSettingsGuard
{
    private const string BackupSuffix = ".autoclaude-backup";

    private const string DefaultSettingsJson = """
        {
          "$schema": "https://json.schemastore.org/claude-code-settings.json",
          "permissions": {}
        }
        """;

    private readonly string _settingsPath;
    private bool _restored;

    private ClaudeSettingsGuard(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public static ClaudeSettingsGuard Install(string workingDirectory, bool allowWrite)
    {
        var claudeDir = Path.Combine(workingDirectory, ".claude");
        var settingsPath = Path.Combine(claudeDir, "settings.local.json");
        var backupPath = settingsPath + BackupSuffix;

        try
        {
            Directory.CreateDirectory(claudeDir);

            // If a leftover backup exists from a previous crashed run, restore it first
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, settingsPath, overwrite: true);
                File.Delete(backupPath);
            }

            // Always create a backup: copy the original if it exists,
            // or a default settings file so Restore() can always safely restore.
            if (File.Exists(settingsPath))
                File.Copy(settingsPath, backupPath, overwrite: true);
            else
                File.WriteAllText(backupPath, DefaultSettingsJson, System.Text.Encoding.UTF8);

            // Now overwrite with our temporary permissions
            var allowList = new List<string>
            {
                "Read(//)",
                "WebFetch",
                "WebSearch",
                "Bash(*)"
            };

            if (allowWrite)
            {
                allowList.Add("Edit(//)");
                allowList.Add("Write(//)");
            }

            File.WriteAllText(settingsPath, BuildSettingsJson(allowList), System.Text.Encoding.UTF8);
        }
        catch
        {
            // Failed to install — restore from backup if we made one
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, settingsPath, overwrite: true);
                    File.Delete(backupPath);
                }
            }
            catch { }
        }

        return new ClaudeSettingsGuard(settingsPath);
    }

    /// <summary>
    /// Restores the original settings file (or deletes the temporary one).
    /// Safe to call multiple times.
    /// </summary>
    public void Restore()
    {
        if (_restored) return;
        _restored = true;

        var backupPath = _settingsPath + BackupSuffix;

        try
        {
            if (!File.Exists(backupPath)) return;

            File.Copy(backupPath, _settingsPath, overwrite: true);
            File.Delete(backupPath);
        }
        catch { }
    }

    private static string BuildSettingsJson(List<string> allowList)
    {
        var items = string.Join(",\n      ", allowList.ConvertAll(s => $"\"{s}\""));
        return $$"""
        {
          "$schema": "https://json.schemastore.org/claude-code-settings.json",
          "permissions": {
            "allow": [
              {{items}}
            ]
          }
        }
        """;
    }
}
