using AutoClaude.Core.Ports;
using Spectre.Console;

namespace AutoClaude.Cli.Commands;

public static class AppSettingsCli
{
    public static int RunWithArgs(IAutoClaudeAppSettings appSettings, string? subcommand, string? value)
    {
        appSettings.Reload();

        var sub = subcommand?.Trim();
        if (string.IsNullOrEmpty(sub))
        {
            PrintSummary(appSettings);
            AnsiConsole.MarkupLine("[dim]CLI:[/] [yellow]autoclaude settings debug on[/] | [yellow]off[/]");
            return 0;
        }

        if (!sub.Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[red]Subcomando desconhecido. Use 'debug' ou execute sem argumentos.[/]");
            return 1;
        }

        var val = value?.Trim();
        if (string.IsNullOrEmpty(val))
        {
            AnsiConsole.MarkupLine($"[dim]debugClaudeCommands:[/] {appSettings.DebugClaudeCommands}");
            return 0;
        }

        if (!TryParseBool(val, out var enabled))
        {
            AnsiConsole.MarkupLine("[red]Valor invalido. Use on, off, true ou false.[/]");
            return 1;
        }

        appSettings.SetDebugClaudeCommands(enabled);
        appSettings.Save();
        appSettings.Reload();
        AnsiConsole.MarkupLine($"[green]debugClaudeCommands definido como[/] {enabled}");
        return 0;
    }

    public static void RunInteractive(IAutoClaudeAppSettings appSettings)
    {
        while (true)
        {
            appSettings.Reload();
            AnsiConsole.MarkupLine("[bold]Configuracoes[/]");
            AnsiConsole.MarkupLine($"[dim]Arquivo:[/] {Markup.Escape(appSettings.SettingsFilePath)}");
            AnsiConsole.MarkupLine($"[dim]debugClaudeCommands:[/] {appSettings.DebugClaudeCommands}");
            AnsiConsole.WriteLine();

            var toggleLabel = appSettings.DebugClaudeCommands
                ? "Desativar log de comandos Claude (stderr)"
                : "Ativar log de comandos Claude (stderr)";

            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]Configuracoes[/]")
                    .AddChoices(toggleLabel, "Voltar"));

            if (action == "Voltar")
                return;

            var newVal = !appSettings.DebugClaudeCommands;
            appSettings.SetDebugClaudeCommands(newVal);
            appSettings.Save();
            appSettings.Reload();
            AnsiConsole.MarkupLine($"[green]Salvo. debugClaudeCommands = {newVal}[/]");
            AnsiConsole.WriteLine();
        }
    }

    private static void PrintSummary(IAutoClaudeAppSettings appSettings)
    {
        AnsiConsole.MarkupLine("[bold]Configuracoes[/]");
        AnsiConsole.MarkupLine($"[dim]Arquivo:[/] {Markup.Escape(appSettings.SettingsFilePath)}");
        AnsiConsole.MarkupLine($"[dim]debugClaudeCommands:[/] {appSettings.DebugClaudeCommands}");
        AnsiConsole.WriteLine();
    }

    private static bool TryParseBool(string s, out bool value)
    {
        value = false;
        if (s.Equals("on", StringComparison.OrdinalIgnoreCase) || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1")
        {
            value = true;
            return true;
        }

        if (s.Equals("off", StringComparison.OrdinalIgnoreCase) || s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0")
        {
            value = false;
            return true;
        }

        return false;
    }
}
