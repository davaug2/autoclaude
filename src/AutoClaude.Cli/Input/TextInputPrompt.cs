using Spectre.Console;

namespace AutoClaude.Cli.Input;

public static class TextInputPrompt
{
    public static string? Read(
        string title,
        string? initial = null,
        bool allowEmpty = false,
        bool allowCancel = true,
        MultilineTextBoxEditor.MultilineEditorMode mode = MultilineTextBoxEditor.MultilineEditorMode.Standard)
    {
        while (true)
        {
            AnsiConsole.WriteLine();
            var draft = MultilineTextBoxEditor.Run(title, initial, mode);
            if (draft == null)
            {
                if (allowCancel)
                    return null;
                continue;
            }

            if (!allowEmpty && string.IsNullOrWhiteSpace(draft))
            {
                AnsiConsole.MarkupLine("[red]O texto nao pode ser vazio.[/]");
                continue;
            }

            AnsiConsole.WriteLine();
            var preview = draft.Length > 4000 ? draft[..4000] + Environment.NewLine + "..." : draft;
            AnsiConsole.Write(
                new Panel(Markup.Escape(preview))
                    .Header("[yellow]Confirme o texto antes de enviar[/]")
                    .Border(BoxBorder.Rounded));

            var choices = new List<string> { "Confirmar", "Editar novamente" };
            if (allowCancel)
                choices.Add("Cancelar");

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("O texto esta correto?")
                    .AddChoices(choices));

            if (choice == "Confirmar")
                return draft;
            if (choice == "Editar novamente")
            {
                initial = draft;
                continue;
            }
            return null;
        }
    }
}
