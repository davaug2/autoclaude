using System.ComponentModel;
using AutoClaude.Core.Ports;
using Spectre.Console.Cli;

namespace AutoClaude.Cli.Commands;

public class SettingsCommand : AsyncCommand<SettingsCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[SUBCOMANDO]")]
        [Description("Use 'debug' para ver ou alterar a flag que imprime o comando completo enviado ao Claude.")]
        public string? Subcommand { get; set; }

        [CommandArgument(1, "[VALOR]")]
        [Description("on, off, true ou false (com subcomando debug)")]
        public string? Value { get; set; }
    }

    private readonly IAutoClaudeAppSettings _appSettings;

    public SettingsCommand(IAutoClaudeAppSettings appSettings)
    {
        _appSettings = appSettings;
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
    {
        var code = AppSettingsCli.RunWithArgs(_appSettings, settings.Subcommand, settings.Value);
        return Task.FromResult(code);
    }
}
