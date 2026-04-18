using System.Text;
using Spectre.Console;
using TextCopy;

namespace AutoClaude.Cli.Input;

public static class MultilineTextBoxEditor
{
    private const int MaxVisibleLines = 14;

    public static string? Run(string title, string? initial, MultilineEditorMode mode)
    {

        var lines = new List<StringBuilder>();
        if (string.IsNullOrEmpty(initial))
            lines.Add(new StringBuilder());
        else
        {
            foreach (var seg in initial.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                lines.Add(new StringBuilder(seg));
            if (lines.Count == 0)
                lines.Add(new StringBuilder());
        }

        var cursorLine = 0;
        var cursorCol = 0;
        var firstVisibleLine = 0;

        var originTop = Console.CursorTop;
        var originLeft = Console.CursorLeft;

        var innerWidth = 40;
        var visibleCount = 8;
        var titleRowCount = 1;

        var lastErrorRow = -1;

        void UpdateScroll()
        {
            if (cursorLine < firstVisibleLine)
                firstVisibleLine = cursorLine;
            if (cursorLine >= firstVisibleLine + visibleCount)
                firstVisibleLine = cursorLine - visibleCount + 1;
            firstVisibleLine = Math.Max(0, Math.Min(firstVisibleLine, Math.Max(0, lines.Count - visibleCount)));
        }

        int ContentRow(int lineIndex)
        {
            return originTop + 1 + titleRowCount + (lineIndex - firstVisibleLine);
        }

        int BottomBorderRow()
        {
            return originTop + 1 + titleRowCount + visibleCount;
        }

        int HintRow()
        {
            return originTop + 1 + titleRowCount + visibleCount + 1;
        }

        void WritePaddedLine(int left, int top, string text)
        {
            try
            {
                var max = Math.Max(0, Console.WindowWidth - left);
                if (text.Length < max)
                    text += new string(' ', max - text.Length);
                else if (text.Length > max)
                    text = text[..max];
                Console.SetCursorPosition(left, top);
                Console.Write(text);
            }
            catch
            {
            }
        }

        void DrawOneContentLine(int lineIndex)
        {
            if (lineIndex < firstVisibleLine || lineIndex >= firstVisibleLine + visibleCount)
                return;
            var content = lineIndex < lines.Count ? lines[lineIndex] : new StringBuilder();
            var isCursorLine = lineIndex == cursorLine;
            var col = isCursorLine ? cursorCol : 0;
            var segment = GetVisibleSegment(content, col, innerWidth);
            var padded = segment.PadRight(innerWidth);
            WritePaddedLine(originLeft, ContentRow(lineIndex), "│" + padded + "│");
        }

        void DrawHintLine()
        {
            var hint = mode == MultilineEditorMode.Interrupt
                ? "Ctrl+Enter confirmar | Enter linha | setas | Ctrl+V | Ctrl+C vazio sai"
                : "Ctrl+Enter confirmar | Enter linha | setas | Ctrl+V | Esc cancela";
            var status = $" | L{cursorLine + 1} C{cursorCol + 1}";
            var combined = " " + hint + status;
            WritePaddedLine(originLeft, HintRow(), combined);
        }

        void DrawFull()
        {
            UpdateScroll();
            var maxUsableWidth = Math.Max(1, Console.WindowWidth - Math.Max(0, originLeft));
            innerWidth = Math.Max(1, maxUsableWidth - 2);

            var maxBottomRow = Console.WindowHeight - 1;
            var maxTitleRows = Math.Max(1, maxBottomRow - originTop - 4);
            var fullTitleLines = WrapTextToLines(title, innerWidth);
            var wrappedTitle = fullTitleLines.Count > maxTitleRows
                ? fullTitleLines.Take(maxTitleRows).ToList()
                : fullTitleLines;
            if (fullTitleLines.Count > maxTitleRows)
            {
                var ell = innerWidth >= 3 ? "..." : "";
                var maxLen = innerWidth - ell.Length;
                var raw = fullTitleLines[maxTitleRows - 1].TrimEnd();
                var cut = raw.Length > maxLen ? raw[..maxLen] : raw;
                wrappedTitle[^1] = (cut + ell).PadRight(innerWidth);
            }

            titleRowCount = wrappedTitle.Count;

            var maxVisible = Math.Max(1, maxBottomRow - originTop - 2 - titleRowCount);
            visibleCount = Math.Min(MaxVisibleLines, maxVisible);
            while (visibleCount > 1 && HintRow() > maxBottomRow)
                visibleCount--;

            WritePaddedLine(originLeft, originTop + 0, "┌" + new string('─', innerWidth) + "┐");
            var row = originTop + 1;
            foreach (var line in wrappedTitle)
            {
                WritePaddedLine(originLeft, row, "│" + line.PadRight(innerWidth) + "│");
                row++;
            }

            for (var i = 0; i < visibleCount; i++)
                DrawOneContentLine(firstVisibleLine + i);

            WritePaddedLine(originLeft, BottomBorderRow(), "└" + new string('─', innerWidth) + "┘");
            DrawHintLine();
            lastErrorRow = -1;
        }

        void DrawPartial(int? prevCursorLine)
        {
            if (prevCursorLine.HasValue && prevCursorLine.Value != cursorLine)
            {
                DrawOneContentLine(prevCursorLine.Value);
                DrawOneContentLine(cursorLine);
            }
            else
                DrawOneContentLine(cursorLine);

            DrawHintLine();
        }

        DrawFull();

        while (true)
        {
            var prevLine = cursorLine;
            var prevFirst = firstVisibleLine;

            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Enter && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (!IsDocumentEmpty(lines))
                    return JoinLines(lines);
                if (mode == MultilineEditorMode.Interrupt)
                {
                    DrawPartial(prevLine);
                    continue;
                }

                var errRow = HintRow() + 1;
                if (errRow < Console.WindowHeight)
                {
                    WritePaddedLine(originLeft, errRow, " Texto vazio. Digite algo ou use Esc.");
                    lastErrorRow = errRow;
                }
                continue;
            }

            if (lastErrorRow >= 0)
            {
                WritePaddedLine(originLeft, lastErrorRow, new string(' ', Math.Min(Console.WindowWidth - originLeft, 80)));
                lastErrorRow = -1;
            }

            if (key.Key == ConsoleKey.Enter && !key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                var tail = lines[cursorLine].ToString(cursorCol, lines[cursorLine].Length - cursorCol);
                lines[cursorLine].Remove(cursorCol, lines[cursorLine].Length - cursorCol);
                lines.Insert(cursorLine + 1, new StringBuilder(tail));
                cursorLine++;
                cursorCol = 0;
                DrawFull();
                continue;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                if (mode == MultilineEditorMode.Interrupt)
                {
                    DrawPartial(prevLine);
                    continue;
                }
                if (IsDocumentEmpty(lines))
                    return null;
                if (!AnsiConsole.Confirm("Descartar o texto digitado?", false))
                {
                    DrawPartial(prevLine);
                    continue;
                }
                return null;
            }

            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (IsDocumentEmpty(lines) && mode == MultilineEditorMode.Interrupt)
                    return null;
                if (!IsDocumentEmpty(lines) && mode == MultilineEditorMode.Interrupt)
                {
                    if (AnsiConsole.Confirm("Descartar e sair sem enviar?", false))
                        return null;
                }
                DrawPartial(prevLine);
                continue;
            }

            if (key.Key == ConsoleKey.V && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                try
                {
                    var clip = ClipboardService.GetText();
                    if (!string.IsNullOrEmpty(clip))
                        InsertPaste(lines, ref cursorLine, ref cursorCol, clip);
                }
                catch
                {
                }
                DrawFull();
                continue;
            }

            var needFull = false;

            if (key.Key == ConsoleKey.LeftArrow)
            {
                if (cursorCol > 0)
                    cursorCol--;
                else if (cursorLine > 0)
                {
                    cursorLine--;
                    cursorCol = lines[cursorLine].Length;
                }
            }
            else if (key.Key == ConsoleKey.RightArrow)
            {
                if (cursorCol < lines[cursorLine].Length)
                    cursorCol++;
                else if (cursorLine < lines.Count - 1)
                {
                    cursorLine++;
                    cursorCol = 0;
                }
            }
            else if (key.Key == ConsoleKey.UpArrow)
            {
                if (cursorLine > 0)
                {
                    cursorLine--;
                    cursorCol = Math.Min(cursorCol, lines[cursorLine].Length);
                }
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                if (cursorLine < lines.Count - 1)
                {
                    cursorLine++;
                    cursorCol = Math.Min(cursorCol, lines[cursorLine].Length);
                }
            }
            else if (key.Key == ConsoleKey.Home)
                cursorCol = 0;
            else if (key.Key == ConsoleKey.End)
                cursorCol = lines[cursorLine].Length;
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (cursorCol > 0)
                {
                    lines[cursorLine].Remove(cursorCol - 1, 1);
                    cursorCol--;
                }
                else if (cursorLine > 0)
                {
                    var prevLen = lines[cursorLine - 1].Length;
                    lines[cursorLine - 1].Append(lines[cursorLine]);
                    lines.RemoveAt(cursorLine);
                    cursorLine--;
                    cursorCol = prevLen;
                    needFull = true;
                }
            }
            else if (key.Key == ConsoleKey.Delete)
            {
                if (cursorCol < lines[cursorLine].Length)
                    lines[cursorLine].Remove(cursorCol, 1);
                else if (cursorLine < lines.Count - 1)
                {
                    lines[cursorLine].Append(lines[cursorLine + 1]);
                    lines.RemoveAt(cursorLine + 1);
                    needFull = true;
                }
            }
            else if (key.Key == ConsoleKey.Tab)
            {
                const string tab = "    ";
                lines[cursorLine].Insert(cursorCol, tab);
                cursorCol += tab.Length;
            }
            else if (key.KeyChar >= 32 && !char.IsControl(key.KeyChar))
            {
                lines[cursorLine].Insert(cursorCol, key.KeyChar);
                cursorCol++;
            }

            UpdateScroll();
            if (needFull || firstVisibleLine != prevFirst)
            {
                DrawFull();
                continue;
            }

            DrawPartial(prevLine);
        }
    }

    private static List<string> WrapTextToLines(string text, int width)
    {
        var result = new List<string>();
        if (width <= 0)
        {
            result.Add("");
            return result;
        }

        if (string.IsNullOrEmpty(text))
        {
            result.Add("");
            return result;
        }

        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                result.Add(new string(' ', width));
                continue;
            }

            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = new StringBuilder();
            foreach (var word in words)
            {
                if (word.Length > width)
                {
                    if (line.Length > 0)
                    {
                        result.Add(line.ToString().PadRight(width));
                        line.Clear();
                    }

                    for (var i = 0; i < word.Length; i += width)
                    {
                        var len = Math.Min(width, word.Length - i);
                        result.Add(word.Substring(i, len).PadRight(width));
                    }
                    continue;
                }

                if (line.Length == 0)
                {
                    line.Append(word);
                    continue;
                }

                if (line.Length + 1 + word.Length <= width)
                    line.Append(' ').Append(word);
                else
                {
                    result.Add(line.ToString().PadRight(width));
                    line.Clear().Append(word);
                }
            }

            if (line.Length > 0)
                result.Add(line.ToString().PadRight(width));
        }

        if (result.Count == 0)
            result.Add(new string(' ', width));
        return result;
    }

    private static void InsertPaste(List<StringBuilder> lines, ref int cursorLine, ref int cursorCol, string text)
    {
        foreach (var ch in text)
        {
            if (ch == '\r')
                continue;
            if (ch == '\n')
            {
                var tail = lines[cursorLine].ToString(cursorCol, lines[cursorLine].Length - cursorCol);
                lines[cursorLine].Remove(cursorCol, lines[cursorLine].Length - cursorCol);
                lines.Insert(cursorLine + 1, new StringBuilder(tail));
                cursorLine++;
                cursorCol = 0;
            }
            else
            {
                lines[cursorLine].Insert(cursorCol, ch);
                cursorCol++;
            }
        }
    }

    private static string GetVisibleSegment(StringBuilder line, int cursorCol, int width)
    {
        if (width <= 0)
            return "";
        if (line.Length <= width)
            return line.ToString();
        var offset = cursorCol <= width / 2
            ? 0
            : Math.Min(cursorCol - width / 2, line.Length - width);
        return line.ToString(offset, Math.Min(width, line.Length - offset));
    }

    private static bool IsDocumentEmpty(List<StringBuilder> lines)
    {
        return lines.All(l => l.Length == 0);
    }

    private static string JoinLines(List<StringBuilder> lines)
    {
        return string.Join(Environment.NewLine, lines.Select(l => l.ToString()));
    }

    public enum MultilineEditorMode
    {
        Standard,
        Interrupt
    }
}
