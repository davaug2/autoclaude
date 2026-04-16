using System.Text;

namespace AutoClaude.Cli;

public sealed class ConsoleTextEditor
{
    private readonly List<StringBuilder> _lines = [new()];
    private readonly string _prefix;
    private readonly bool _allowCtrlCCancel;
    private readonly bool _multiLine;
    private int _row;
    private int _col;

    public ConsoleTextEditor(string prefix = "    > ", bool allowCtrlCCancel = false, bool multiLine = false)
    {
        _prefix = prefix;
        _allowCtrlCCancel = allowCtrlCCancel;
        _multiLine = multiLine;
    }

    public string? Run()
    {
        Console.Write(_prefix);

        while (true)
        {
            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (HandleCtrlC()) return null;
                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    var result = HandleEnter();
                    if (result != null) return result;
                    break;
                case ConsoleKey.Backspace:
                    HandleBackspace();
                    break;
                case ConsoleKey.Delete:
                    HandleDelete();
                    break;
                case ConsoleKey.LeftArrow:
                    MoveLeft();
                    break;
                case ConsoleKey.RightArrow:
                    MoveRight();
                    break;
                case ConsoleKey.UpArrow:
                    MoveUp();
                    break;
                case ConsoleKey.DownArrow:
                    MoveDown();
                    break;
                case ConsoleKey.Home:
                    MoveHome();
                    break;
                case ConsoleKey.End:
                    MoveEnd();
                    break;
                default:
                    if (key.KeyChar >= ' ')
                        InsertChar(key.KeyChar);
                    break;
            }
        }
    }

    private bool HandleCtrlC()
    {
        if (!_allowCtrlCCancel) return false;

        var line = _lines[_row];
        if (line.Length > 0)
        {
            ClearVisualLine();
            line.Clear();
            _col = 0;
            return false;
        }

        if (_lines.All(l => l.Length == 0))
        {
            Console.WriteLine();
            return true;
        }

        ClearVisualLine();
        line.Clear();
        _col = 0;
        return false;
    }

    private string? HandleEnter()
    {
        var line = _lines[_row];

        if (!_multiLine)
        {
            Console.WriteLine();
            var text = line.ToString().Trim();
            return text.Length > 0 ? text : "";
        }

        if (line.Length == 0 && _lines.Any(l => l.Length > 0))
        {
            Console.WriteLine();
            _lines.RemoveAt(_row);
            return string.Join("\n", _lines.Select(l => l.ToString()));
        }

        var tail = line.ToString(_col, line.Length - _col);
        line.Remove(_col, line.Length - _col);

        if (tail.Length > 0)
        {
            Console.Write("\x1b[K");
        }

        Console.WriteLine();
        _row++;
        _lines.Insert(_row, new StringBuilder(tail));
        _col = 0;

        if (_row < _lines.Count - 1)
            RedrawLinesBelow();

        Console.Write($"\r{_prefix}");
        if (tail.Length > 0)
        {
            Console.Write(tail);
            Console.Write(new string('\b', tail.Length));
        }

        return null;
    }

    private void HandleBackspace()
    {
        var line = _lines[_row];

        if (_col > 0)
        {
            line.Remove(--_col, 1);
            Console.Write('\b');
            RedrawFromCursor();
        }
        else if (_multiLine && _row > 0)
        {
            var currentText = line.ToString();
            _lines.RemoveAt(_row);
            _row--;
            var prevLine = _lines[_row];
            _col = prevLine.Length;
            prevLine.Append(currentText);

            Console.Write("\x1b[A");
            Console.Write($"\r{_prefix}");
            Console.Write(prevLine.ToString());
            Console.Write("\x1b[K");

            var backCount = prevLine.Length - _col;
            if (backCount > 0) Console.Write(new string('\b', backCount));

            RedrawLinesBelow();
            RepositionCursor();
        }
    }

    private void HandleDelete()
    {
        var line = _lines[_row];

        if (_col < line.Length)
        {
            line.Remove(_col, 1);
            RedrawFromCursor();
        }
        else if (_multiLine && _row < _lines.Count - 1)
        {
            var nextLine = _lines[_row + 1];
            line.Append(nextLine.ToString());
            _lines.RemoveAt(_row + 1);

            RedrawFromCursor();
            RedrawLinesBelow();
            RepositionCursor();
        }
    }

    private void MoveLeft()
    {
        if (_col > 0)
        {
            _col--;
            Console.Write('\b');
        }
        else if (_multiLine && _row > 0)
        {
            _row--;
            _col = _lines[_row].Length;
            Console.Write("\x1b[A");
            Console.Write($"\r{_prefix}");
            Console.Write(_lines[_row].ToString());
        }
    }

    private void MoveRight()
    {
        var line = _lines[_row];
        if (_col < line.Length)
        {
            Console.Write(line[_col]);
            _col++;
        }
        else if (_multiLine && _row < _lines.Count - 1)
        {
            _row++;
            _col = 0;
            Console.Write("\x1b[B");
            Console.Write($"\r{_prefix}");
        }
    }

    private void MoveUp()
    {
        if (!_multiLine || _row <= 0) return;
        _row--;
        _col = Math.Min(_col, _lines[_row].Length);
        Console.Write("\x1b[A");
        Console.Write($"\r{_prefix}");
        var text = _lines[_row].ToString();
        Console.Write(text);
        Console.Write("\x1b[K");
        var back = text.Length - _col;
        if (back > 0) Console.Write(new string('\b', back));
    }

    private void MoveDown()
    {
        if (!_multiLine || _row >= _lines.Count - 1) return;
        _row++;
        _col = Math.Min(_col, _lines[_row].Length);
        Console.Write("\x1b[B");
        Console.Write($"\r{_prefix}");
        var text = _lines[_row].ToString();
        Console.Write(text);
        Console.Write("\x1b[K");
        var back = text.Length - _col;
        if (back > 0) Console.Write(new string('\b', back));
    }

    private void MoveHome()
    {
        if (_col > 0)
        {
            Console.Write(new string('\b', _col));
            _col = 0;
        }
    }

    private void MoveEnd()
    {
        var line = _lines[_row];
        if (_col < line.Length)
        {
            Console.Write(line.ToString(_col, line.Length - _col));
            _col = line.Length;
        }
    }

    private void InsertChar(char c)
    {
        var line = _lines[_row];
        line.Insert(_col, c);
        var tail = line.ToString(_col, line.Length - _col);
        Console.Write(tail);
        _col++;
        var back = line.Length - _col;
        if (back > 0) Console.Write(new string('\b', back));
    }

    private void ClearVisualLine()
    {
        Console.Write($"\r{_prefix}");
        Console.Write("\x1b[K");
    }

    private void RedrawFromCursor()
    {
        var line = _lines[_row];
        var tail = line.ToString(_col, line.Length - _col);
        Console.Write(tail + " ");
        Console.Write(new string('\b', tail.Length + 1));
    }

    private void RedrawLinesBelow()
    {
        var savedRow = _row;
        var savedCol = _col;

        for (var i = _row + 1; i < _lines.Count; i++)
        {
            Console.Write("\x1b[B");
            Console.Write($"\r{_prefix}{_lines[i]}");
            Console.Write("\x1b[K");
        }

        Console.Write("\x1b[B");
        Console.Write("\r\x1b[K");

        var linesToGoUp = _lines.Count - savedRow;
        if (linesToGoUp > 0)
            Console.Write($"\x1b[{linesToGoUp}A");
    }

    private void RepositionCursor()
    {
        Console.Write($"\r{_prefix}");
        if (_col > 0)
            Console.Write(_lines[_row].ToString(0, _col));
    }
}
