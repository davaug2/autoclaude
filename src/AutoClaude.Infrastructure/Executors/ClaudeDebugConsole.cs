using Spectre.Console;

namespace AutoClaude.Infrastructure.Executors;

internal static class ClaudeDebugConsole
{
    public static void WriteCommandPanel(string workingDirectory, string arguments)
    {
        int consoleWidth;
        try { consoleWidth = Console.WindowWidth; }
        catch { consoleWidth = 80; }
        var width = Math.Clamp(consoleWidth - 2, 44, 120);

        var stderr = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Detect,
            Out = new AnsiConsoleOutput(Console.Error)
        });

        var body = new Rows(
            new Markup($"[grey]cwd[/] [white]{Markup.Escape(workingDirectory)}[/]"),
            new Text(""),
            new Markup("[grey]linha de comando[/]"),
            new Text(""),
            new Markup($"[white]{Markup.Escape(arguments)}[/]"));

        var panel = new Panel(body)
            .Header("[bold white on darkorange_1] DEBUG — comando Claude [/]")
            .Border(BoxBorder.Double)
            .BorderStyle(new Style(Color.Orange1, decoration: Decoration.Bold))
            .Padding(1, 1);
        panel.Width = width;

        stderr.Write(panel);
        stderr.WriteLine();
    }
}
