using AutoClaude.Core.Domain.Enums;
using AutoClaude.Core.Domain.Models;
using Spectre.Console;

namespace AutoClaude.Cli.Rendering;

public static class SessionTableRenderer
{
    public static void WriteAllowedDirectoriesSection(Session session)
    {
        AnsiConsole.MarkupLine("[bold]Diretorios permitidos nesta sessao:[/]");
        if (session.AllowedDirectories.Count > 0)
        {
            foreach (var dir in session.AllowedDirectories)
            {
                var exists = Directory.Exists(dir) ? "[green]OK[/]" : "[red]NAO ENCONTRADO[/]";
                AnsiConsole.MarkupLine($"  {Markup.Escape(dir)} {exists}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(session.TargetPath))
        {
            AnsiConsole.MarkupLine($"  [dim]Nenhum na lista persistida; caminho principal:[/] {Markup.Escape(session.TargetPath!)}");
        }
        else
            AnsiConsole.MarkupLine("  [dim](nenhum registrado)[/]");

        AnsiConsole.WriteLine();
    }

    public static void Render(IReadOnlyList<Session> sessions)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]ID[/]")
            .AddColumn("[bold]Nome[/]")
            .AddColumn("[bold]Objetivo[/]")
            .AddColumn("[bold]Status[/]")
            .AddColumn("[bold]Fase[/]")
            .AddColumn("[bold]Criada em[/]");

        foreach (var session in sessions)
        {
            var statusColor = session.Status switch
            {
                SessionStatus.Completed => "green",
                SessionStatus.Failed => "red",
                SessionStatus.Running => "yellow",
                SessionStatus.Paused => "blue",
                _ => "dim"
            };

            table.AddRow(
                session.Id.ToString()[..8],
                Markup.Escape(session.Name ?? "-"),
                Markup.Escape(Truncate(session.Objective ?? "-", 40)),
                $"[{statusColor}]{session.Status}[/]",
                session.CurrentPhaseOrdinal.ToString(),
                session.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
        }

        AnsiConsole.Write(table);
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }
}
